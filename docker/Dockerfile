# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# SpectreAgent — hardened image for running the GitHub Copilot CLI safely.
#
# Security properties:
#   • Runs the CLI as the unprivileged 'agent' user (UID 1000).
#   • Requires a host directory to be bind-mounted at /workspace so the CLI
#     cannot access files outside that path.
#   • Applies iptables rules on startup (via entrypoint.sh + firewall.sh) to
#     allow only outbound DNS and HTTPS, mitigating malware exfiltration risk.
#   • Run with --cap-drop ALL --cap-add NET_ADMIN for the firewall to work.
#   • Use --security-opt no-new-privileges:true to prevent privilege escalation.
# ---------------------------------------------------------------------------

FROM ubuntu:22.04

# Prevent apt from prompting for input during package installation
ENV DEBIAN_FRONTEND=noninteractive

# ---------------------------------------------------------------------------
# System packages
# ---------------------------------------------------------------------------
RUN apt-get update && apt-get install -y --no-install-recommends \
        # Firewall
        iptables \
        # Privilege-drop helper used by entrypoint
        gosu \
        # General utilities
        curl \
        ca-certificates \
        git \
    && rm -rf /var/lib/apt/lists/*

# ---------------------------------------------------------------------------
# GitHub CLI
# ---------------------------------------------------------------------------
RUN curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
        | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg \
    && chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
        | tee /etc/apt/sources.list.d/github-cli.list > /dev/null \
    && apt-get update \
    && apt-get install -y --no-install-recommends gh \
    && rm -rf /var/lib/apt/lists/*

# ---------------------------------------------------------------------------
# Unprivileged user
# ---------------------------------------------------------------------------
RUN useradd -m -u 1000 -s /bin/bash agent

# ---------------------------------------------------------------------------
# GitHub Copilot CLI extension (installed for the 'agent' user)
# ---------------------------------------------------------------------------
USER agent
RUN --mount=type=secret,id=GH_TOKEN,env=GH_TOKEN \
    gh extension install github/gh-copilot --force
USER root

# ---------------------------------------------------------------------------
# Workspace — callers MUST bind-mount a host directory here at runtime.
# The entrypoint enforces this and aborts if no bind-mount is found.
# ---------------------------------------------------------------------------
RUN mkdir -p /workspace && chown agent:agent /workspace
VOLUME ["/workspace"]

# ---------------------------------------------------------------------------
# Scripts
# ---------------------------------------------------------------------------
COPY firewall.sh    /usr/local/bin/firewall.sh
COPY entrypoint.sh  /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/firewall.sh /usr/local/bin/entrypoint.sh

# ---------------------------------------------------------------------------
# Runtime
# ---------------------------------------------------------------------------
WORKDIR /workspace

# Entrypoint runs as root to apply firewall rules, then drops to 'agent'
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]

# Default: show Copilot CLI help (users pass only the sub-command, e.g. suggest / explain)
CMD ["--help"]
