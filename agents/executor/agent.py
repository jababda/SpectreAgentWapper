"""
Executor Agent
==============
Reads the plan markdown produced by the planner agent and carries out
the described file operations.

Environment variables (set by the host CLI):
  GITHUB_TOKEN   – Personal access token with 'copilot' scope
  COPILOT_MODEL  – Model name (e.g. gpt-4o)
  AGENT_PROMPT   – Path to the plan markdown file (inside the container)
  PLAN_FILE      – Same as AGENT_PROMPT for the executor
  IPC_DIR        – Path to the IPC directory used for permission requests
"""

import os
import sys
import json
import re
import textwrap
from pathlib import Path

sys.path.insert(0, "/agents/shared")

from copilot_client import CopilotClient  # noqa: E402
from permissions import request_permission  # noqa: E402

GITHUB_TOKEN = os.environ["GITHUB_TOKEN"]
MODEL = os.environ.get("COPILOT_MODEL", "gpt-4o")
PLAN_FILE = Path(os.environ["PLAN_FILE"])
IPC_DIR = Path(os.environ.get("IPC_DIR", "/workspace/.agent-ipc"))
WORKSPACE = Path("/workspace")


SYSTEM_PROMPT = textwrap.dedent("""
    You are a senior software engineer acting as an **executor agent**.
    You will receive an implementation plan written in Markdown and the
    current content of each file that needs to change.

    For each file operation described in the plan respond with a JSON
    object on a single line with the following schema:

    {"op": "create"|"modify"|"delete", "path": "<relative path>", "content": "<full new file content or null for delete>"}

    Emit one JSON object per line. Do not output any other text.
    Use forward slashes in paths. Paths are relative to the repository root.
    When modifying a file, provide the *complete* new file content (not a diff).
""").strip()


def read_file_safe(path: Path) -> str:
    """Read a file, returning empty string on error."""
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return ""


def collect_relevant_files(plan_text: str) -> dict[str, str]:
    """
    Parse the plan for mentioned file paths and read their current content.
    Returns a mapping of relative_path -> current_content.

    Matches backtick-quoted paths from the plan, including files without
    extensions (Makefile, Dockerfile, LICENSE, etc.) and files in subdirectories.
    """
    paths: set[str] = set()
    # Match any backtick-quoted token that looks like a file path:
    #   - Contains at least one path separator, OR
    #   - Has a file extension, OR
    #   - Is a known extensionless filename
    _EXTENSIONLESS_NAMES = frozenset({
        "Makefile", "makefile", "Dockerfile", "Containerfile",
        "LICENSE", "LICENCE", "README", "CHANGELOG", "AUTHORS",
        "CONTRIBUTING", "Procfile", "Vagrantfile",
    })
    for match in re.finditer(r"`([^`\n]+)`", plan_text):
        candidate = match.group(1)
        # Must look like a file path (has / or . or is a known bare name)
        if "/" in candidate or "." in candidate or candidate in _EXTENSIONLESS_NAMES:
            full = WORKSPACE / candidate
            if full.exists() and full.is_file():
                paths.add(candidate)

    result: dict[str, str] = {}
    for p in sorted(paths):
        result[p] = read_file_safe(WORKSPACE / p)
    return result


def apply_operation(op: dict, client: CopilotClient) -> None:
    """Apply a single file operation, requesting permission if needed."""
    operation = op.get("op", "")
    rel_path = op.get("path", "").replace("\\", "/").lstrip("/")
    content = op.get("content")
    target = WORKSPACE / rel_path

    if operation == "delete":
        if target.exists():
            target.unlink()
            print(f"[executor] Deleted {rel_path}", flush=True)
        return

    # For create / modify, we need to write a file
    if not target.exists():
        # New file – request permission
        request_permission(
            file_path=str(target),
            directory=str(target.parent),
            reason=f"Creating new file as part of the plan: {rel_path}",
            ipc_dir=IPC_DIR,
        )

    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content or "", encoding="utf-8")
    verb = "Created" if operation == "create" else "Modified"
    print(f"[executor] {verb} {rel_path}", flush=True)


def main() -> None:
    if not PLAN_FILE.exists():
        print(f"[executor] Plan file not found: {PLAN_FILE}", file=sys.stderr)
        sys.exit(1)

    plan_text = PLAN_FILE.read_text(encoding="utf-8")
    print(f"[executor] Loaded plan from {PLAN_FILE}", flush=True)

    relevant_files = collect_relevant_files(plan_text)

    files_context = "\n\n".join(
        f"### {path}\n```\n{content}\n```"
        for path, content in relevant_files.items()
    ) or "(no existing files matched)"

    user_message = textwrap.dedent(f"""
        ## Implementation plan
        {plan_text}

        ## Current file contents
        {files_context}
    """).strip()

    client = CopilotClient(token=GITHUB_TOKEN, model=MODEL)
    print(f"[executor] Requesting implementation from {MODEL} …", flush=True)

    raw_response = client.chat(system=SYSTEM_PROMPT, user=user_message)

    # Parse the JSON-per-line response
    errors: list[str] = []
    for line in raw_response.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            op = json.loads(line)
            apply_operation(op, client)
        except json.JSONDecodeError as exc:
            errors.append(f"Could not parse line: {line!r} – {exc}")

    if errors:
        print("[executor] Warnings during execution:", file=sys.stderr)
        for err in errors:
            print(f"  {err}", file=sys.stderr)

    print("[executor] Done.", flush=True)


if __name__ == "__main__":
    main()
