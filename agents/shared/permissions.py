"""
File-creation permission interceptor.

When an agent wants to create a new file it calls `request_permission()`.
This function writes a JSON request to the IPC directory and then polls
for a response written by the CLI running on the host.

If the IPC directory is not available the function defaults to granting
permission (useful for testing outside the container).
"""

import json
import os
import sys
import time
import uuid
from pathlib import Path


def request_permission(
    file_path: str,
    directory: str,
    reason: str,
    ipc_dir: Path,
    poll_interval: float = 0.3,
    timeout: float = 300.0,
) -> bool:
    """
    Request permission from the host CLI to create *file_path*.

    Returns True if permission is granted, raises PermissionError otherwise.
    """
    requests_dir = ipc_dir / "requests"
    responses_dir = ipc_dir / "responses"

    if not requests_dir.exists() or not responses_dir.exists():
        # IPC not available – grant by default (e.g. running outside container)
        return True

    request_id = str(uuid.uuid4())
    request_file = requests_dir / f"{request_id}.json"
    response_file = responses_dir / f"{request_id}.response"

    payload = {
        "id": request_id,
        "filePath": file_path,
        "directory": directory,
        "reason": reason,
    }

    request_file.write_text(json.dumps(payload), encoding="utf-8")
    print(
        f"[permission] Waiting for host approval to create: {file_path}",
        file=sys.stderr,
        flush=True,
    )

    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if response_file.exists():
            try:
                response = json.loads(response_file.read_text(encoding="utf-8"))
                granted: bool = response.get("granted", False)
            except Exception:
                granted = False

            if granted:
                return True
            else:
                raise PermissionError(
                    f"Host denied permission to create file: {file_path}"
                )

        time.sleep(poll_interval)

    # Timeout – clean up and raise
    try:
        request_file.unlink(missing_ok=True)
    except Exception:
        pass
    raise TimeoutError(
        f"Timed out waiting for permission to create file: {file_path}"
    )
