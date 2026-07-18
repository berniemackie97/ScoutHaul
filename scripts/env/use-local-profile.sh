#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "usage: $0 development|test|production [--allow-production]" >&2
  exit 2
fi

PROFILE="$1"
ALLOW="${2:-}"

case "$PROFILE" in
  development|test) ;;
  production)
    if [[ "$ALLOW" != "--allow-production" ]]; then
      echo "Refusing to activate production locally without --allow-production." >&2
      exit 2
    fi
    ;;
  *)
    echo "Unknown profile: $PROFILE" >&2
    exit 2
    ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TARGETS=(
  "services/api"
  "apps/admin"
  "apps/storefront"
  "apps/mobile"
)

umask 077

for target in "${TARGETS[@]}"; do
  src="$ROOT/$target/.env.$PROFILE.local"
  dst="$ROOT/$target/.env"

  if [[ ! -f "$src" ]]; then
    echo "Missing $src" >&2
    exit 1
  fi

  cp "$src" "$dst"
  chmod 600 "$dst"
  echo "activated $target/.env from $target/.env.$PROFILE.local"
done
