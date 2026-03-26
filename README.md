# SpectreAgent

A CLI tool that runs hardened GitHub Copilot agents against a local repository inside a locked-down Podman container.

## Overview

SpectreAgent orchestrates two AI agents:

| Agent | Role |
|-------|------|
| **Planner** | Reads your change request and the repository, then writes a detailed, human-readable Markdown plan. |
| **Executor** | Reads the plan and mechanically applies the described file changes. |

Both agents run inside a hardened Podman container that:

* **Restricts the filesystem** – only the mounted repository is accessible.
* **Restricts the network** – outbound traffic is blocked by `iptables`/`ip6tables` except for DNS and explicitly whitelisted HTTPS domains.
* **Drops privileges** – the agent process runs as an unprivileged user inside the container.
* **Prompts for permission** – whenever an agent wants to **create a new file**, the CLI pauses and asks you to Allow, Allow-Directory, Allow-All, or Deny.

---

## Prerequisites

| Tool | Purpose |
|------|---------|
| [.NET 8+](https://dotnet.microsoft.com/download) | Build / run the CLI |
| [Podman](https://podman.io/docs/installation) | Run the hardened container |
| [GitHub CLI (`gh`)](https://cli.github.com/) | (Optional) retrieve a GitHub token automatically |

---

## Quick Start

### 1. Build the CLI

```bash
cd src/Spectre.Agent
dotnet build
dotnet run -- --help
```

Or publish a self-contained binary:

```bash
dotnet publish -c Release -r linux-x64 --self-contained -o ~/.local/bin
```

### 2. Build the container image

```bash
podman build -t spectre-agent:latest -f container/Containerfile .
```

### 3. Configure credentials

```bash
spectre-agent setup
```

This will:
1. Try to read a GitHub token via `gh auth token`.
2. If that fails, prompt you to enter a token manually.
3. Save the configuration to `~/.spectre-agent/config.json` (owner-read-only).

You can also pass the token directly:

```bash
spectre-agent setup --token ghp_xxxx
```

### 4. Run both agents

```bash
spectre-agent run "Add a health-check endpoint to the API"
```

SpectreAgent will:
1. Run the **planner** agent → writes `.spectre-plan.md` in the current directory.
2. Show you the plan path and start the **executor** agent.
3. Prompt you for permission every time a new file would be created.

---

## Commands

```
spectre-agent setup      Configure GitHub token and model
spectre-agent run        Run planner then executor (full pipeline)
spectre-agent plan       Run only the planner agent
spectre-agent execute    Run only the executor agent against a plan file
spectre-agent whitelist  Add a domain to the network allowlist
```

### `setup`

```
spectre-agent setup [--token <TOKEN>] [--model <MODEL>]
```

| Flag | Default | Description |
|------|---------|-------------|
| `--token` | *(prompted)* | GitHub PAT with `copilot` scope |
| `--model` | `gpt-4o` | Copilot model to use |

### `run`

```
spectre-agent run <PROMPT> [--repo <PATH>] [--plan-file <FILE>] [--approve-all]
```

| Flag | Default | Description |
|------|---------|-------------|
| `<PROMPT>` | *(required)* | Natural-language change description |
| `--repo` | Current directory | Repository root to mount |
| `--plan-file` | `.spectre-plan.md` | Where to write/read the plan |
| `--approve-all` | false | Skip all permission prompts |

### `plan`

```
spectre-agent plan <PROMPT> [--repo <PATH>] [--plan-file <FILE>]
```

Runs only the planner agent and writes a Markdown plan. Useful for reviewing the plan before committing to execution.

### `execute`

```
spectre-agent execute <PLAN_FILE> [--repo <PATH>] [--approve-all]
```

Runs only the executor agent against an existing plan file.

### `whitelist`

```
spectre-agent whitelist <DOMAIN>
```

Adds a domain to the outbound network allowlist stored in `~/.spectre-agent/config.json`. The following domains are always allowed inside the container so the agents can reach GitHub Copilot:

* `api.github.com`
* `copilot-proxy.githubusercontent.com`
* `githubcopilot.com`

---

## File-creation permission prompts

When an agent tries to create a new file the CLI pauses and presents a prompt:

```
[Permission request] The agent wants to create a file:
  Path: /path/to/new-file.ts
  Reason: Creating new controller as part of the plan

? How do you want to proceed?
> Allow this file
  Allow all files in /path/to/
  Allow all (approve everything from now on)
  Deny
```

| Choice | Effect |
|--------|--------|
| **Allow this file** | Permits only this specific file. |
| **Allow all files in …** | Permits all future files in that directory. |
| **Allow all** | Disables prompts for the rest of the run (same as `--approve-all`). |
| **Deny** | Rejects the request; the agent will raise `PermissionError` and abort. |

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
├── src/
│   └── Spectre.Agent/       # C# CLI (Spectre.Console.Cli)
│       ├── Commands/        # setup, run, plan, execute, whitelist
│       └── Services/        # ContainerService, ConfigService, GithubTokenService
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