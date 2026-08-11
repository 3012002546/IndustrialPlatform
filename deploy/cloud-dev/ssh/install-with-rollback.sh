#!/usr/bin/env bash
set -Eeuo pipefail

target_user="${1:?Usage: $0 <verified-ssh-user> <confirmation-file>}"
confirmation_file="${2:?Usage: $0 <verified-ssh-user> <confirmation-file>}"
source_file="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/00-industrial-platform-hardening.conf"
target_file=/etc/ssh/sshd_config.d/00-industrial-platform-hardening.conf
backup_file="${target_file}.rollback"

[[ "$(id -u)" -eq 0 ]] || { echo "Run through sudo." >&2; exit 1; }
user_home="$(getent passwd "$target_user" | cut -d: -f6)"
[[ -n "$user_home" && -s "$user_home/.ssh/authorized_keys" ]] || {
  echo "Refusing hardening: verified user has no authorized_keys." >&2
  exit 1
}

rm -f -- "$confirmation_file"
if [[ -f "$target_file" ]]; then
  cp -a -- "$target_file" "$backup_file"
else
  rm -f -- "$backup_file"
fi

rollback() {
  if [[ -f "$backup_file" ]]; then
    mv -f -- "$backup_file" "$target_file"
  else
    rm -f -- "$target_file"
  fi
  sshd -t
  systemctl reload ssh
}
trap rollback ERR

install -o root -g root -m 0644 "$source_file" "$target_file"
sshd -t
systemctl reload ssh

for _ in {1..30}; do
  if [[ -f "$confirmation_file" ]]; then
    rm -f -- "$confirmation_file" "$backup_file"
    trap - ERR
    echo "SSH hardening confirmed by a second session."
    exit 0
  fi
  sleep 1
done

echo "No second-session confirmation; rolling back." >&2
rollback
trap - ERR
exit 1
