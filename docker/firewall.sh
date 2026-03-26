#!/bin/bash
# Configures iptables firewall rules to mitigate the risk of malware exfiltrating
# data or establishing unexpected inbound connections.
set -euo pipefail

# Flush all existing rules and delete user-defined chains
iptables -F
iptables -X
iptables -Z

# Default policy: drop all traffic unless explicitly allowed
iptables -P INPUT   DROP
iptables -P FORWARD DROP
iptables -P OUTPUT  DROP

# Allow unrestricted loopback traffic
iptables -A INPUT  -i lo -j ACCEPT
iptables -A OUTPUT -o lo -j ACCEPT

# Allow return traffic for connections we initiated
iptables -A INPUT  -m state --state ESTABLISHED,RELATED -j ACCEPT
iptables -A OUTPUT -m state --state ESTABLISHED,RELATED -j ACCEPT

# Allow outbound DNS (UDP + TCP) so hostname resolution works
iptables -A OUTPUT -p udp --dport 53 -j ACCEPT
iptables -A OUTPUT -p tcp --dport 53 -j ACCEPT

# Allow outbound HTTPS only — all GitHub API / Copilot traffic uses port 443
iptables -A OUTPUT -p tcp --dport 443 -j ACCEPT
