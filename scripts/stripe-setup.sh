#!/usr/bin/env bash
# Stripe setup for the storefront, as code rather than dashboard clicks.
#
# Idempotent: re-running reconciles the webhook endpoint (same URL = updated, not duplicated) and re-prints the
# environment variables the API needs. Safe to run against test or live — the mode follows the key you pass.
#
#   ./scripts/stripe-setup.sh sk_test_...        # test mode
#   ./scripts/stripe-setup.sh rk_live_...        # go-live
#
# Never commit a key. Never paste one into a chat or a ticket. This script reads it from your argument or the
# environment and writes results to services/api/.env, which is gitignored.
set -euo pipefail

KEY="${1:-${STRIPE_API_KEY:-}}"
if [[ -z "$KEY" ]]; then
  echo "usage: $0 <stripe secret or restricted key>" >&2
  exit 2
fi

API="https://scouthaul-production.up.railway.app"
WEBHOOK_URL="$API/api/storefront/stripe/webhook"
EVENTS=(checkout.session.completed checkout.session.expired)
ENV_FILE="$(cd "$(dirname "$0")/.." && pwd)/services/api/.env"

case "$KEY" in
  sk_test_*|rk_test_*) MODE="TEST" ;;
  sk_live_*|rk_live_*) MODE="LIVE" ;;
  pk_*) echo "That is a publishable key. This needs a server key (sk_/rk_)." >&2; exit 2 ;;
  *) echo "Unrecognized key prefix." >&2; exit 2 ;;
esac

echo "mode: $MODE"

# The account must answer before anything else is attempted.
if ! curl -sf https://api.stripe.com/v1/balance -u "$KEY:" >/dev/null; then
  echo "Stripe rejected that key." >&2
  exit 1
fi
echo "key:  accepted"

# One endpoint per URL. Reuse the existing one so re-runs don't pile up duplicates that all fire at once.
EXISTING=$(curl -s https://api.stripe.com/v1/webhook_endpoints -u "$KEY:" -G -d limit=100 \
  | python3 -c "import json,sys; print(next((e['id'] for e in json.load(sys.stdin).get('data',[]) if e['url']=='$WEBHOOK_URL'), ''))")

ARGS=(-d "url=$WEBHOOK_URL" -d "description=ScoutHaul storefront — order finalization ($MODE)")
for e in "${EVENTS[@]}"; do ARGS+=(-d "enabled_events[]=$e"); done

if [[ -n "$EXISTING" ]]; then
  # A secret is only ever returned at creation. An existing endpoint's secret must come from the dashboard,
  # so only the event list is reconciled here.
  curl -s "https://api.stripe.com/v1/webhook_endpoints/$EXISTING" -u "$KEY:" \
    "${ARGS[@]/-d url=$WEBHOOK_URL/}" >/dev/null
  echo "webhook: $EXISTING (existing, events reconciled)"
  echo
  echo "Its signing secret was shown only when it was created. If you don't have it, roll it:"
  echo "  https://dashboard.stripe.com/${MODE,,}/webhooks/$EXISTING"
else
  RESULT=$(curl -s https://api.stripe.com/v1/webhook_endpoints -u "$KEY:" "${ARGS[@]}")
  ID=$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['id'])" "$RESULT")
  SECRET=$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['secret'])" "$RESULT")
  echo "webhook: $ID (created)"

  if [[ "$MODE" == "TEST" && -f "$ENV_FILE" ]]; then
    python3 - "$ENV_FILE" "$SECRET" <<'PY'
import sys
path, secret = sys.argv[1], sys.argv[2]
lines = [l for l in open(path).read().rstrip('\n').split('\n') if not l.startswith('Stripe__WebhookSecret=')]
lines.append('Stripe__WebhookSecret=' + secret)
open(path, 'w').write('\n'.join(lines) + '\n')
PY
    echo "local:   Stripe__WebhookSecret written to services/api/.env"
  fi

  echo
  echo "Set these in Railway (Variables), then it redeploys itself:"
  echo "  Stripe__ApiKey        = <the $MODE key you just used>"
  echo "  Stripe__WebhookSecret = $SECRET"
fi

echo
echo "tax registrations (Stripe collects \$0 in any jurisdiction without an active one):"
curl -s https://api.stripe.com/v1/tax/registrations -u "$KEY:" -G -d limit=20 | python3 -c "
import json,sys
d = json.load(sys.stdin).get('data', [])
print('\n'.join(f\"  {r['country']}/{r.get('state','-')}  {r['status']}\" for r in d) if d else '  (none — no tax will be collected)')
"

cat <<'NOTE'

Reminders that have bitten this project already:
  · The API key and the webhook secret must be the SAME mode. A live key with a test secret rejects every event.
  · Test and live are separate endpoints with separate secrets. Go-live = re-run this with the live key.
  · Railway variable names use a DOUBLE underscore: Stripe__ApiKey, not Stripe_ApiKey or STRIPE_API_KEY.
NOTE
