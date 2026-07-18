#!/usr/bin/env python3
"""Provision and reconcile ScoutHaul Railway environments without printing secrets."""

from __future__ import annotations

import argparse
import base64
import getpass
import json
import os
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SERVICE_API = "ScoutHaul"
SERVICE_DB = "Postgres"
ENVS = {
    "production": "https://scouthaul-production.up.railway.app",
    "development": "https://scouthaul-development.up.railway.app",
    "test": "https://scouthaul-test.up.railway.app",
}
WEBHOOK_EVENTS = ["checkout.session.completed", "checkout.session.expired"]
LOCAL_PRODUCTION_API_KEYS = [
    "ASPNETCORE_ENVIRONMENT",
    "ASPNETCORE_HTTP_PORTS",
    "Authentication__Supabase__InviteRedirectUrl",
    "Authentication__Supabase__ServiceRoleKey",
    "Authentication__Supabase__Url",
    "ConnectionStrings__Postgres",
    "Database__ApplyMigrationsOnStartup",
    "Gemini__ApiKey",
    "Gemini__Model",
    "GoogleCloudVision__ApiKey",
    "RateLimiting__PermitLimit",
    "RateLimiting__WindowSeconds",
    "Stripe__ApiKey",
    "Stripe__WebhookSecret",
    "StorefrontCheckout__AllowedShipCountries__0",
    "StorefrontCheckout__AutomaticTaxEnabled",
    "StorefrontCheckout__FlatShippingAmount",
    "StorefrontCheckout__FreeShippingThreshold",
    "StorefrontCheckout__LocalPickupEnabled",
    "StorefrontCheckout__PublicApiBaseUrl",
    "StorefrontCheckout__SessionExpiryMinutes",
]


def run(args: list[str], *, stdin: str | None = None, sensitive_stdin: bool = False) -> str:
    result = subprocess.run(args, input=stdin, text=True, capture_output=True, check=False)
    if result.returncode != 0:
        if sensitive_stdin:
            raise RuntimeError(f"{args[0]} failed while handling secret input")
        message = result.stderr.strip() or result.stdout.strip() or f"{args[0]} failed"
        raise RuntimeError(message)
    return result.stdout


def railway_json(args: list[str]) -> object:
    return json.loads(run(["railway", *args, "--json"]))


def var_map(service: str, environment: str) -> dict[str, str]:
    data = railway_json(["variable", "list", "--service", service, "--environment", environment])
    if isinstance(data, dict) and all(isinstance(value, str) for value in data.values()):
        return data
    if isinstance(data, dict):
        rows = data.get("variables", [])
    else:
        rows = data
    return {row.get("name") or row.get("key"): row.get("value") for row in rows if row.get("name") or row.get("key")}


def set_var(service: str, environment: str, key: str, value: str, *, skip_deploys: bool = True) -> None:
    args = [
        "railway",
        "variable",
        "set",
        key,
        "--stdin",
        "--service",
        service,
        "--environment",
        environment,
    ]
    if skip_deploys:
        args.append("--skip-deploys")
    args.append("--json")
    run(args, stdin=value, sensitive_stdin=True)


def ensure_environment(name: str) -> None:
    data = railway_json(["environment", "list"])
    existing = {row["name"] for row in data["environments"]}
    if name in existing:
        print(f"railway {name}: exists")
        return
    railway_json(["environment", "new", name, "--duplicate", "production"])
    print(f"railway {name}: created from production")


def assert_services(environment: str) -> None:
    services = railway_json(["service", "list", "--environment", environment])
    names = {row["name"] for row in services}
    missing = {SERVICE_API, SERVICE_DB} - names
    if missing:
        raise RuntimeError(f"{environment} missing Railway services: {', '.join(sorted(missing))}")


def connection_string(pg: dict[str, str]) -> str:
    required = ["PGHOST", "PGPORT", "PGDATABASE", "PGUSER", "PGPASSWORD"]
    missing = [key for key in required if not pg.get(key)]
    if missing:
        raise RuntimeError(f"Postgres variables missing: {', '.join(missing)}")
    return (
        f"Host={pg['PGHOST']};"
        f"Port={pg['PGPORT']};"
        f"Database={pg['PGDATABASE']};"
        f"Username={pg['PGUSER']};"
        f"Password={pg['PGPASSWORD']};"
        "SSL Mode=Require;Trust Server Certificate=true"
    )


def ensure_db_binding(environment: str) -> None:
    api = var_map(SERVICE_API, environment)
    pg = var_map(SERVICE_DB, environment)
    current = api.get("ConnectionStrings__Postgres") or ""
    if all((pg.get(key) or "") in current for key in ["PGHOST", "PGPORT", "PGDATABASE", "PGUSER", "PGPASSWORD"]):
        print(f"railway {environment}: API bound to env-local Postgres")
        return
    set_var(SERVICE_API, environment, "ConnectionStrings__Postgres", connection_string(pg))
    print(f"railway {environment}: API DB binding repaired")


def parse_env(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    if not path.exists():
        return values
    for raw in path.read_text().splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip().strip("\"'")
    return values


def stripe_request(key: str, method: str, path: str, params: dict[str, str | list[str]] | None = None) -> dict:
    encoded: list[tuple[str, str]] = []
    for name, value in (params or {}).items():
        if isinstance(value, list):
            encoded.extend((name, item) for item in value)
        else:
            encoded.append((name, value))
    body = urllib.parse.urlencode(encoded).encode()
    url = f"https://api.stripe.com/v1/{path.lstrip('/')}"
    if method == "GET" and body:
        url = f"{url}?{body.decode()}"
        body = None
    request = urllib.request.Request(url, data=body if method != "GET" else None, method=method)
    token = base64.b64encode(f"{key}:".encode()).decode()
    request.add_header("Authorization", f"Basic {token}")
    request.add_header("Content-Type", "application/x-www-form-urlencoded")
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.loads(response.read().decode())
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode()
        try:
            error = json.loads(detail).get("error", {}).get("message", detail)
        except json.JSONDecodeError:
            error = detail
        raise RuntimeError(f"Stripe API rejected request: {error}") from exc


def normalize_key(raw: str) -> str:
    value = raw.strip()
    if "=" in value:
        value = value.split("=", 1)[1].strip()
    return value


def key_mode(key: str) -> str:
    if key.startswith(("sk_live_", "rk_live_")):
        return "live"
    if key.startswith(("sk_test_", "rk_test_")):
        return "test"
    raise RuntimeError("Stripe key must start with sk_live_, rk_live_, sk_test_, or rk_test_.")


def read_clipboard_secret() -> str | None:
    try:
        value = run(["pbpaste"]).strip()
    except Exception:
        return None
    value = normalize_key(value)
    return value or None


def ensure_stripe_webhook(stripe_key: str, environment: str, supplied_secret: str | None = None) -> str:
    mode = key_mode(stripe_key)
    url = f"{ENVS[environment]}/api/storefront/stripe/webhook"
    stripe_request(stripe_key, "GET", "balance")
    endpoints = stripe_request(stripe_key, "GET", "webhook_endpoints", {"limit": "100"}).get("data", [])
    existing = next((item for item in endpoints if item.get("url") == url), None)

    params = {
        "description": f"ScoutHaul {environment} storefront order finalization",
        "enabled_events[]": WEBHOOK_EVENTS,
    }

    if existing:
        stripe_request(stripe_key, "POST", f"webhook_endpoints/{existing['id']}", params)
        if supplied_secret:
            print(f"stripe {environment}: existing webhook reconciled")
            return supplied_secret
        current = var_map(SERVICE_API, environment).get("Stripe__WebhookSecret")
        if current:
            print(f"stripe {environment}: existing webhook reconciled; kept existing Railway signing secret")
            return current
        raise RuntimeError(
            f"Stripe already has a {mode} webhook for {url}, but Stripe does not reveal old signing secrets. "
            "Pass it with --production-webhook-secret or recreate/roll the endpoint in Stripe."
        )

    created = stripe_request(
        stripe_key,
        "POST",
        "webhook_endpoints",
        {
            "url": url,
            "description": f"ScoutHaul {environment} storefront order finalization",
            "enabled_events[]": WEBHOOK_EVENTS,
        },
    )
    secret = created.get("secret")
    if not secret:
        raise RuntimeError("Stripe did not return a signing secret for the new webhook.")
    print(f"stripe {environment}: webhook created")
    return secret


def set_checkout_defaults(environment: str) -> None:
    api_url = ENVS[environment]
    values = {
        "ASPNETCORE_ENVIRONMENT": "Production",
        "ASPNETCORE_HTTP_PORTS": "8080",
        "Database__ApplyMigrationsOnStartup": "true",
        "StorefrontCheckout__PublicApiBaseUrl": api_url,
        "StorefrontCheckout__FlatShippingAmount": "9.95",
        "StorefrontCheckout__FreeShippingThreshold": "150",
        "StorefrontCheckout__LocalPickupEnabled": "true",
        "StorefrontCheckout__AutomaticTaxEnabled": "true",
        "StorefrontCheckout__AllowedShipCountries__0": "US",
        "StorefrontCheckout__SessionExpiryMinutes": "30",
    }
    for key, value in values.items():
        set_var(SERVICE_API, environment, key, value)
    print(f"railway {environment}: runtime checkout defaults set")


def set_stripe(environment: str, key: str, secret: str) -> None:
    set_var(SERVICE_API, environment, "Stripe__ApiKey", key)
    set_var(SERVICE_API, environment, "Stripe__WebhookSecret", secret)
    print(f"railway {environment}: Stripe variables set")


def write_local_production_api_profile() -> None:
    source = var_map(SERVICE_API, "production")
    values = {key: source[key] for key in LOCAL_PRODUCTION_API_KEYS if source.get(key)}
    values["HAULSCOUT_ENV"] = "production"
    values["ASPNETCORE_ENVIRONMENT"] = "Production"
    path = ROOT / "services/api/.env.production.local"
    path.write_text("\n".join(f"{key}={values[key]}" for key in sorted(values)) + "\n")
    os.chmod(path, 0o600)
    print("local services/api/.env.production.local written from Railway production")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--ensure-railway-envs", action="store_true")
    parser.add_argument("--sync-production-stripe", action="store_true")
    parser.add_argument("--production-live-key-from-clipboard", action="store_true")
    parser.add_argument("--production-live-key-from-railway", action="store_true")
    parser.add_argument("--production-webhook-secret")
    parser.add_argument("--sync-test-stripe", action="store_true")
    parser.add_argument("--write-local-production-api-env", action="store_true")
    parser.add_argument("--redeploy", action="store_true")
    args = parser.parse_args()

    should_sync_railway = (
        args.ensure_railway_envs
        or args.sync_production_stripe
        or args.sync_test_stripe
        or args.redeploy
    )

    if should_sync_railway:
        for environment in ("development", "test"):
            ensure_environment(environment)

        for environment in ENVS:
            assert_services(environment)
            ensure_db_binding(environment)
            set_checkout_defaults(environment)

    local_env = parse_env(ROOT / "services/api/.env")
    test_key = normalize_key(os.environ.get("STRIPE_TEST_API_KEY") or local_env.get("Stripe__ApiKey", ""))
    if args.sync_test_stripe and test_key:
        if key_mode(test_key) != "test":
            raise RuntimeError("Refusing to use a non-test Stripe key for development/test.")
        for environment in ("development", "test"):
            secret = ensure_stripe_webhook(test_key, environment)
            set_stripe(environment, test_key, secret)

    if args.sync_production_stripe:
        live_key = normalize_key(os.environ.get("STRIPE_LIVE_API_KEY", ""))
        if not live_key and args.production_live_key_from_clipboard:
            live_key = read_clipboard_secret() or ""
        if not live_key and args.production_live_key_from_railway:
            live_key = normalize_key(var_map(SERVICE_API, "production").get("Stripe__ApiKey", ""))
        if not live_key and sys.stdin.isatty():
            live_key = normalize_key(getpass.getpass("Stripe live secret key: "))
        if key_mode(live_key) != "live":
            raise RuntimeError("Refusing to use a non-live Stripe key for production.")
        secret = ensure_stripe_webhook(live_key, "production", args.production_webhook_secret)
        set_stripe("production", live_key, secret)

    if args.redeploy:
        for environment in ENVS:
            run(["railway", "redeploy", "--service", SERVICE_API, "--environment", environment, "--yes", "--json"])
            print(f"railway {environment}: redeploy requested")

    if args.write_local_production_api_env:
        write_local_production_api_profile()

    print("Railway environment sync complete; no secrets were printed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
