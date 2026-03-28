# GitHub Copilot Instructions

## Repository Overview

This repository contains:

1. **`docker/`** — A hardened Ubuntu-based Docker image that runs the GitHub Copilot CLI
   (`gh copilot`) as an unprivileged user with iptables firewall restrictions.
2. **`src/CopilotWrapper/`** — A .NET 8 console application (using [Spectre.Console](https://spectreconsole.net/))
   that provides a friendly menu-driven wrapper for launching the copilot container.

---

## .NET Application (`src/CopilotWrapper`)

### Project conventions

- **Framework**: `net8.0`
- **Language**: C# with nullable reference types and implicit usings enabled.
- **UI library**: [Spectre.Console](https://spectreconsole.net/) — use `AnsiConsole` for all
  terminal output, prompts, and progress displays.
- **Working directory**: The app uses `Environment.CurrentDirectory` (the directory the user
  invoked the executable from), **not** the directory the binary lives in.
- **Publish targets**: `linux-x64` and `win-x64` as self-contained single-file executables.

### Building

```bash
# Debug build
dotnet build src/CopilotWrapper/CopilotWrapper.csproj

# Release build
dotnet build src/CopilotWrapper/CopilotWrapper.csproj --configuration Release

# Publish self-contained single-file for Linux
dotnet publish src/CopilotWrapper/CopilotWrapper.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --output ./publish/linux-x64

# Publish self-contained single-file for Windows
dotnet publish src/CopilotWrapper/CopilotWrapper.csproj \
  --configuration Release \
  --runtime win-x64 \
  --output ./publish/win-x64
```

### PR checks

The workflow `.github/workflows/build-dotnet.yml` runs on every pull request to `main` and:

1. Restores NuGet dependencies.
2. Builds the project in Release configuration.
3. Publishes self-contained single-file binaries for `linux-x64` and `win-x64`.
4. Uploads both binaries as GitHub Actions artifacts.

---

## Docker Image (`docker/`)

### Hardening properties

- Runs the CLI as the unprivileged `agent` user (UID 1000).
- Requires a host directory bind-mounted at `/workspace`.
- Applies iptables rules (allow DNS + HTTPS only) via `entrypoint.sh` + `firewall.sh`.
- Must be run with `--cap-drop ALL --cap-add NET_ADMIN --security-opt no-new-privileges:true`.

### PR checks

The workflow `.github/workflows/build.yml` runs on every pull request to `main` and builds the
Docker image (without pushing) to verify the `Dockerfile` is valid.

### Publishing

The workflow `.github/workflows/publish.yml` builds **and pushes** the image to
`ghcr.io/jababda/spectreagentWapper` on pushes to `main` or on version tags (`v*`).

---

## General guidelines for Copilot

- Keep security properties of the Docker image intact — never relax the firewall rules or
  the workspace mount requirement.
- Use `AnsiConsole` (Spectre.Console) for all terminal output in the .NET project; do not use
  `Console.WriteLine` directly.
- When adding new NuGet packages, check the GitHub Advisory Database for known vulnerabilities
  before committing.
- All new GitHub Actions workflows should trigger on `pull_request` to `main` as a minimum.
