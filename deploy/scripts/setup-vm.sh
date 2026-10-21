#!/usr/bin/env bash
#
# Prepares a fresh Ubuntu 24.04 VM to run Promuse. Once, as root; safe to run again - every step
# checks before it changes anything.
#
#   sudo deploy/scripts/setup-vm.sh "<the deploy key's public line: ssh-ed25519 AAAA... >"
#
# Run from /srv/promuse/deploy (a copy of this repository's deploy/ folder): the timers it
# installs run the scripts from there.
#
#   1. Docker Engine and Compose, from Docker's own apt repository (docs.docker.com/engine/install/ubuntu)
#   2. a `deploy` user for the pipeline: SSH key only, in the docker group
#   3. SSH: no passwords, no root login
#   4. firewall (ufw): 22, 80 and 443 in, nothing else
#   5. fail2ban against SSH guessing; unattended-upgrades for security updates, daily
#   6. /srv/promuse, owned by deploy
#   7. timers: the nightly backup, and DuckDNS every five minutes
#
# 等于 root / The docker group is root on this machine in all but name - whoever can start a
# container can mount /. That is what the deploy key is trusted with, so keep it only in GitHub.

set -euo pipefail

[ "$(id -u)" = "0" ] || { echo "run as root: sudo $0 '<public key>'" >&2; exit 1; }
public_key="${1:?usage: setup-vm.sh '<the deploy user public key line>'}"

ROOT=/srv/promuse
DEPLOY_USER=deploy
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

log() { printf '== %s\n' "$*"; }

# ---------------------------------------------------------------------------------- 1. Docker
if ! command -v docker >/dev/null 2>&1; then
    log "installing Docker"
    apt-get update
    apt-get install -y ca-certificates curl
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    cat > /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF
    apt-get update
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
else
    log "Docker is installed"
fi
systemctl enable --now docker

# ---------------------------------------------------------------------------------- 2. deploy user
if ! id "$DEPLOY_USER" >/dev/null 2>&1; then
    log "creating $DEPLOY_USER"
    useradd --create-home --shell /bin/bash "$DEPLOY_USER"
fi
usermod -aG docker "$DEPLOY_USER"
install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "/home/$DEPLOY_USER/.ssh"
keys="/home/$DEPLOY_USER/.ssh/authorized_keys"
touch "$keys"
grep -qxF "$public_key" "$keys" || printf '%s\n' "$public_key" >> "$keys"
chown "$DEPLOY_USER:$DEPLOY_USER" "$keys"
chmod 600 "$keys"

# ---------------------------------------------------------------------------------- 3. SSH
log "SSH: keys only"
cat > /etc/ssh/sshd_config.d/10-promuse.conf <<'EOF'
# Written by deploy/scripts/setup-vm.sh.
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
EOF
sshd -t
systemctl reload ssh

# ---------------------------------------------------------------------------------- 4. firewall
log "firewall: 22, 80, 443"
apt-get install -y ufw
ufw default deny incoming >/dev/null
ufw default allow outgoing >/dev/null
ufw allow 22/tcp >/dev/null
ufw allow 80/tcp >/dev/null
ufw allow 443/tcp >/dev/null
ufw allow 443/udp >/dev/null
ufw --force enable >/dev/null
# Docker writes its own iptables rules for published ports, ahead of ufw's: only Caddy publishes
# any (80, 443), which are open anyway. Never publish another port in the compose files.

# ---------------------------------------------------------------------------------- 5. updates, fail2ban
log "fail2ban and unattended-upgrades"
apt-get install -y fail2ban unattended-upgrades
systemctl enable --now fail2ban
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF

# ---------------------------------------------------------------------------------- 6. /srv/promuse
log "$ROOT"
install -d -m 755 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$ROOT" "$ROOT/metrics"
install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$ROOT/edge" "$ROOT/staging" "$ROOT/production" "$ROOT/backups"
# The copy of deploy/ these scripts run from, put here by root: the pipeline replaces it as deploy.
[ -d "$ROOT/deploy" ] && chown -R "$DEPLOY_USER:$DEPLOY_USER" "$ROOT/deploy"

# ---------------------------------------------------------------------------------- 7. timers
log "timers: backup nightly, DuckDNS every five minutes"
for unit in promuse-backup.service promuse-backup.timer promuse-duckdns.service promuse-duckdns.timer; do
    install -m 644 "$here/systemd/$unit" "/etc/systemd/system/$unit"
done
systemctl daemon-reload
systemctl enable --now promuse-backup.timer promuse-duckdns.timer

log "done. Next: the edge's .env and edge.sh, then a deploy (deploy/README.md)"
