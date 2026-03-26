# CopilotAgent

A testing ground for running hardened GitHub Copilot agents against a local repository inside a locked-down Podman container.

## Overview

CopilotAgent orchestrates two AI agents:

| Agent | Role |
|-------|------|
| **Planner** | Reads your change request and the repository, then writes a detailed, human-readable Markdown plan. |
| **Executor** | Reads the plan and mechanically applies the described file changes. |

Both agents run inside a hardened Podman container that:

* **Restricts the filesystem** – only the mounted repository is accessible.
* **Restricts the network** – outbound traffic is blocked by `iptables`/`ip6tables` except for DNS and explicitly whitelisted HTTPS domains.
* **Drops privileges** – the agent process runs as an unprivileged user inside the container.
* **Prompts for permission** – whenever an agent wants to **create a new file**, the host must grant approval via the IPC mechanism.

---

## Prerequisites

| Tool | Purpose |
|------|---------|
| [Podman](https://podman.io/docs/installation) | Run the hardened container |
| [GitHub CLI (`gh`)](https://cli.github.com/) | (Optional) retrieve a GitHub token automatically |

---

## Quick Start

### 1. Build the container image

```bash
podman build -t copilot-agent:latest -f container/Containerfile .
```

### 2. Set environment variables

```bash
export GITHUB_TOKEN=ghp_xxxx        # GitHub PAT with 'copilot' scope
export COPILOT_MODEL=gpt-4o         # Optional, defaults to gpt-4o
```

### 3. Run the planner agent

```bash
podman run --rm -it \
  --cap-drop=ALL --cap-add=NET_ADMIN --security-opt=no-new-privileges \
  --network=bridge \
  --volume "$(pwd):/workspace:Z" \
  --volume "$(pwd)/agents:/agents:ro,Z" \
  --env GITHUB_TOKEN \
  --env COPILOT_MODEL \
  --env AGENT_NAME=planner \
  --env "AGENT_PROMPT=Add a health-check endpoint to the API" \
  --env PLAN_FILE=/workspace/.agent-plan.md \
  copilot-agent:latest
```

### 4. Review and run the executor agent

```bash
# Review the generated plan
cat .agent-plan.md

# Run executor against the plan
podman run --rm -it \
  --cap-drop=ALL --cap-add=NET_ADMIN --security-opt=no-new-privileges \
  --network=bridge \
  --volume "$(pwd):/workspace:Z" \
  --volume "$(pwd)/agents:/agents:ro,Z" \
  --env GITHUB_TOKEN \
  --env COPILOT_MODEL \
  --env AGENT_NAME=executor \
  --env PLAN_FILE=/workspace/.agent-plan.md \
  copilot-agent:latest
```

---

## File-creation permission IPC

When an agent tries to create a new file it writes a JSON request to `.agent-ipc/requests/` and waits for a response in `.agent-ipc/responses/`. The host process is responsible for reading requests and writing responses:

```json
// request: .agent-ipc/requests/<uuid>.json
{ "id": "...", "filePath": "/workspace/src/health.ts", "directory": "/workspace/src", "reason": "Creating new controller" }

// response: .agent-ipc/responses/<uuid>.response
{ "granted": true }
```

If the IPC directory is not present the agents default to granting permission automatically.

---

## Container security

The container uses the following hardening measures:

* `--cap-drop=ALL` – all Linux capabilities dropped.
* `--cap-add=NET_ADMIN` – temporarily added only to configure `iptables` at startup, then the entrypoint drops to an unprivileged user.
* `--security-opt=no-new-privileges` – prevents privilege escalation inside the container.
* `iptables` OUTPUT chain – DROP by default; only DNS and HTTPS to whitelisted IPs are allowed.
* Filesystem isolation – only the repository directory and the read-only agents directory are mounted.

---

## Project structure

```
.
├── agents/
│   ├── planner/             # Python planner agent
│   ├── executor/            # Python executor agent
│   └── shared/              # Shared utilities (Copilot client, permissions IPC)
├── container/
│   ├── Containerfile        # Podman image definition
│   ├── entrypoint.sh        # Container startup script
│   └── firewall.sh          # iptables firewall configuration
└── README.md
```

---

## GitHub token scopes

The GitHub Personal Access Token needs the **`copilot`** scope to call the Copilot API.

Create one at <https://github.com/settings/tokens/new?scopes=copilot>.

---

## License

See [LICENSE](LICENSE).