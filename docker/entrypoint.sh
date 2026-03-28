#!/bin/bash
# Container entrypoint:
#   1. Applies firewall rules (requires NET_ADMIN capability).
#   2. Validates that the host has bind-mounted a directory at /workspace so
#      the Copilot CLI cannot access files outside that path.
#   3. Drops privileges and runs the requested command as the unprivileged
#      'agent' user inside /workspace.
set -euo pipefail

# ---------------------------------------------------------------------------
# 1. Firewall
# ---------------------------------------------------------------------------
if [ "$(id -u)" = "0" ]; then
    echo "[entrypoint] Applying firewall rules..."
    /usr/local/bin/firewall.sh
    echo "[entrypoint] Firewall rules applied."
else
    echo "[entrypoint] WARNING: not running as root — skipping firewall setup." >&2
fi

# ---------------------------------------------------------------------------
# 2. Workspace mount validation
# ---------------------------------------------------------------------------
# Require that the caller has bind-mounted a host directory at /workspace.
# A plain 'docker run' without -v would leave /workspace as an empty
# anonymous volume; we detect a real bind-mount using findmnt which
# handles octal-escaped paths in /proc/mounts correctly.
if ! findmnt --mountpoint /workspace --noheadings > /dev/null 2>&1; then
    echo "" >&2
    echo "[entrypoint] ERROR: /workspace is not mounted from the host." >&2
    echo "  Please provide a bind-mount when running the container, e.g.:" >&2
    echo "    docker run --rm -v \"\$(pwd):/workspace\" ghcr.io/OWNER/spectreagent suggest \"<your question>\"" >&2
    echo "" >&2
    exit 1
fi

echo "[entrypoint] Workspace mounted at /workspace — OK."

# ---------------------------------------------------------------------------
# 3. Drop to unprivileged user and execute the requested command
# ---------------------------------------------------------------------------
cd /workspace
exec gosu agent gh copilot "$@"
