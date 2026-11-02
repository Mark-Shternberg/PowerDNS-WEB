#!/usr/bin/env bash
set -euo pipefail
[[ "$(id -u)" == 0 ]] || { echo "Run as root." >&2; exit 1; }
. /etc/os-release
[[ "${ID:-} ${ID_LIKE:-}" =~ (debian|ubuntu) ]] || { echo "Only Debian-based distributions are supported." >&2; exit 1; }
case "${1:-}" in
  "") exec apt-get install powerdns-web ;;
  -update|--update) exec apt-get install --only-upgrade powerdns-web ;;
  *) echo "Usage: $0 [--update]. Configure the signed APT_Admin repository first." >&2; exit 2 ;;
esac
