#!/usr/bin/env bash
# =============================================================================
# Spectre Agent container entrypoint
# =============================================================================
# Execution flow:
#   1. (root) Configure iptables firewall
#   2. (root) Drop to unprivileged 'agent' user via exec su-exec / gosu / su
#   3. (agent) Run the appropriate Python agent script
# =============================================================================

set -euo pipefail

AGENT_NAME="${AGENT_NAME:?AGENT_NAME environment variable is required}"
IPC_DIR="${IPC_DIR:-/workspace/.spectre-ipc}"
AGENTS_DIR="/agents"

# ── Step 1: Configure firewall (runs as root) ─────────────────────────────────
echo "[entrypoint] Configuring firewall …"
/usr/local/bin/firewall.sh "${IPC_DIR}/allowed-domains.txt" || {
    echo "[entrypoint] WARNING: firewall setup failed – continuing without network restrictions"
}

# ── Step 2: Select agent script ───────────────────────────────────────────────
case "${AGENT_NAME}" in
    planner)
        AGENT_SCRIPT="${AGENTS_DIR}/planner/agent.py"
        ;;
    executor)
        AGENT_SCRIPT="${AGENTS_DIR}/executor/agent.py"
        ;;
    *)
        echo "[entrypoint] ERROR: unknown agent '${AGENT_NAME}'. Use 'planner' or 'executor'." >&2
        exit 1
        ;;
esac

if [[ ! -f "${AGENT_SCRIPT}" ]]; then
    echo "[entrypoint] ERROR: agent script not found: ${AGENT_SCRIPT}" >&2
    exit 1
fi

# ── Step 3: Drop privileges and run agent ─────────────────────────────────────
echo "[entrypoint] Starting ${AGENT_NAME} agent as user 'agent' …"
exec su -s /bin/bash agent -c "python3 '${AGENT_SCRIPT}'"
