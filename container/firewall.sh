#!/usr/bin/env bash
# =============================================================================
# Firewall setup for the Spectre Agent container
# =============================================================================
# This script is executed as root BEFORE dropping privileges to the agent user.
# It configures iptables to:
#   1. Allow loopback traffic
#   2. Allow DNS (UDP/TCP port 53) to the system resolver
#   3. Allow outbound HTTPS (port 443) only to explicitly whitelisted domains
#   4. Block all other outbound traffic
#
# Usage:
#   firewall.sh [allowed-domains-file]
#
# The allowed-domains file contains one domain per line.
# =============================================================================

set -euo pipefail

ALLOWED_DOMAINS_FILE="${1:-/workspace/.spectre-ipc/allowed-domains.txt}"

# ── Flush existing rules ──────────────────────────────────────────────────────
iptables  -F OUTPUT 2>/dev/null || true
iptables  -F INPUT  2>/dev/null || true
ip6tables -F OUTPUT 2>/dev/null || true
ip6tables -F INPUT  2>/dev/null || true

# ── Allow loopback ────────────────────────────────────────────────────────────
iptables  -A OUTPUT -o lo -j ACCEPT
iptables  -A INPUT  -i lo -j ACCEPT
ip6tables -A OUTPUT -o lo -j ACCEPT
ip6tables -A INPUT  -i lo -j ACCEPT

# ── Allow established/related connections (responses to our requests) ─────────
iptables  -A INPUT  -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
ip6tables -A INPUT  -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT

# ── Allow DNS ─────────────────────────────────────────────────────────────────
iptables  -A OUTPUT -p udp --dport 53 -j ACCEPT
iptables  -A OUTPUT -p tcp --dport 53 -j ACCEPT
ip6tables -A OUTPUT -p udp --dport 53 -j ACCEPT
ip6tables -A OUTPUT -p tcp --dport 53 -j ACCEPT

# ── Allow whitelisted domains over HTTPS ─────────────────────────────────────
# NOTE: IP addresses are resolved once at container startup. If a domain's IPs
# change during the container's lifetime the rules may become stale. For most
# short-lived agent runs this is acceptable; for long-running containers,
# consider restarting the container to refresh the allowlist.
if [[ -f "$ALLOWED_DOMAINS_FILE" ]]; then
    while IFS= read -r domain || [[ -n "$domain" ]]; do
        # Strip comments and blank lines
        domain="${domain%%#*}"
        domain="${domain// /}"
        [[ -z "$domain" ]] && continue

        echo "[firewall] Allowing HTTPS to: ${domain}"

        # Resolve all IPs for this domain and allow HTTPS to each
        for ip in $(getent ahosts "$domain" 2>/dev/null | awk '{print $1}' | sort -u); do
            if [[ "$ip" == *":"* ]]; then
                ip6tables -A OUTPUT -d "$ip" -p tcp --dport 443 -j ACCEPT
            else
                iptables  -A OUTPUT -d "$ip" -p tcp --dport 443 -j ACCEPT
            fi
        done
    done < "$ALLOWED_DOMAINS_FILE"
fi

# ── Default DENY for outbound ─────────────────────────────────────────────────
iptables  -A OUTPUT -j DROP
ip6tables -A OUTPUT -j DROP

echo "[firewall] Rules applied."
