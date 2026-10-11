#!/usr/bin/env bash
set -euo pipefail
usage() { echo "usage: [SSH_KEY=path] $0 user@host [linux-x64|linux-arm64]" >&2; exit 2; }
target="${1:-}"; [ -n "$target" ] || usage
rid="${2:-linux-arm64}"
here="$(cd "$(dirname "$0")" && pwd)"
root="$here/../.."
ssh_opts=(-o ConnectTimeout=20)
[ -n "${SSH_KEY:-}" ] && ssh_opts+=(-i "$SSH_KEY")
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH"
publish="$root/dist/relay-$rid"
rm -rf "$publish"
dotnet publish "$root/src/FamilyTimeMultiplayer.Relay" -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true -p:InvariantGlobalization=true -o "$publish" -nologo -v quiet
scp "${ssh_opts[@]}" "$publish/ftmp-relay" "$here/ftmp-relay.service" "$target:/tmp/"
ssh "${ssh_opts[@]}" "$target" 'set -e
  sudo install -D -m 0755 /tmp/ftmp-relay /opt/ftmp-relay/ftmp-relay
  sudo install -m 0644 /tmp/ftmp-relay.service /etc/systemd/system/ftmp-relay.service
  rule="-p tcp -m state --state NEW -m tcp --dport 27780 -j ACCEPT"
  if command -v iptables >/dev/null && ! sudo iptables -C INPUT $rule 2>/dev/null; then
    sudo iptables -I INPUT $rule
  fi
  v4=/etc/iptables/rules.v4
  if [ -f "$v4" ] && ! sudo grep -qF -- "--dport 27780 -j ACCEPT" "$v4"; then
    sudo cp "$v4" "$v4.bak-ftmp-relay"
    if sudo grep -q "^-A INPUT -j REJECT" "$v4"; then
      sudo sed -i "0,/^-A INPUT -j REJECT/s//-A INPUT $rule\n&/" "$v4"
    else
      sudo sed -i "0,/^COMMIT/s//-A INPUT $rule\n&/" "$v4"
    fi
  fi
  if command -v firewall-cmd >/dev/null; then sudo firewall-cmd --permanent --add-port=27780/tcp && sudo firewall-cmd --reload; fi
  sudo systemctl daemon-reload
  sudo systemctl enable --now ftmp-relay
  sudo systemctl restart ftmp-relay
  sleep 1
  systemctl --no-pager --lines=5 status ftmp-relay'
echo "relay deployed; set Network.RelayAddress to the server's public address (port 27780 must be open in the cloud firewall too)"
