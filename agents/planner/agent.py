"""
Planner Agent
=============
Reads the AGENT_PROMPT environment variable, queries the GitHub Copilot / Models
API, and writes a structured markdown plan to PLAN_FILE.

Environment variables (set by the Spectre CLI):
  GITHUB_TOKEN   – Personal access token with 'copilot' scope
  COPILOT_MODEL  – Model name (e.g. gpt-4o)
  AGENT_PROMPT   – Natural-language change request
  PLAN_FILE      – Absolute path where the plan markdown should be written
  IPC_DIR        – Path to the IPC directory used for permission requests
"""

import os
import sys
import json
import textwrap
from pathlib import Path

# Add shared utilities to path
sys.path.insert(0, "/agents/shared")

from copilot_client import CopilotClient  # noqa: E402
from permissions import request_permission  # noqa: E402

GITHUB_TOKEN = os.environ["GITHUB_TOKEN"]
MODEL = os.environ.get("COPILOT_MODEL", "gpt-4o")
PROMPT = os.environ["AGENT_PROMPT"]
PLAN_FILE = Path(os.environ["PLAN_FILE"])
IPC_DIR = Path(os.environ.get("IPC_DIR", "/workspace/.spectre-ipc"))
WORKSPACE = Path("/workspace")

# Maximum number of file paths to include in the repo summary sent to the model.
# Keeping this bounded prevents exceeding the model's context window on large repos.
MAX_REPO_FILES = 200


SYSTEM_PROMPT = textwrap.dedent("""
    You are a senior software engineer acting as a **planning agent**.
    Your job is to analyse the user's change request and the repository
    structure, then produce a detailed, step-by-step implementation plan.

    The plan MUST be written as a Markdown document with the following
    sections:

    # Plan: <short title>

    ## Summary
    One or two paragraphs describing the goal and overall approach.

    ## Repository Overview
    Brief description of the relevant parts of the repo.

    ## Steps
    A numbered list of concrete, actionable steps. Each step must include:
    - The file(s) to create, modify, or delete.
    - What change to make and why.

    ## Files to Create
    A list of new files that will be created. Each entry:
    - `path/to/file` – brief description

    ## Files to Modify
    A list of existing files that will be changed. Each entry:
    - `path/to/file` – brief description of the change

    ## Files to Delete
    A list of files that will be removed (may be empty).

    ## Testing
    How to verify the changes work correctly.

    Be precise, concrete, and complete. The plan will be read by an
    **executor agent** that will mechanically carry out the changes, so
    leave no ambiguity.
""").strip()


def build_repo_summary(root: Path) -> str:
    """Return a compact textual summary of the repository structure."""
    lines: list[str] = []
    for path in sorted(root.rglob("*")):
        rel = path.relative_to(root)
        parts = rel.parts
        # Skip hidden directories / files and common noise
        if any(p.startswith(".") for p in parts):
            continue
        if any(p in ("__pycache__", "node_modules", "obj", "bin") for p in parts):
            continue
        if path.is_file():
            lines.append(str(rel))
        if len(lines) >= MAX_REPO_FILES:
            lines.append("… (truncated)")
            break
    return "\n".join(lines)


def main() -> None:
    client = CopilotClient(token=GITHUB_TOKEN, model=MODEL)

    repo_summary = build_repo_summary(WORKSPACE)

    user_message = textwrap.dedent(f"""
        ## Change request
        {PROMPT}

        ## Repository file tree
        ```
        {repo_summary}
        ```
    """).strip()

    print(f"[planner] Sending request to {MODEL} …", flush=True)

    plan_text = client.chat(
        system=SYSTEM_PROMPT,
        user=user_message,
    )

    # Request permission to write the plan file
    request_permission(
        file_path=str(PLAN_FILE),
        directory=str(PLAN_FILE.parent),
        reason="Writing the implementation plan",
        ipc_dir=IPC_DIR,
    )

    PLAN_FILE.parent.mkdir(parents=True, exist_ok=True)
    PLAN_FILE.write_text(plan_text, encoding="utf-8")

    print(f"[planner] Plan written to {PLAN_FILE}", flush=True)


if __name__ == "__main__":
    main()
