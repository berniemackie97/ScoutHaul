#!/usr/bin/env python3
"""Create gitignored local env profile files without printing secret values."""

from __future__ import annotations

import argparse
import os
import re
import stat
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TARGETS = [
    ROOT / "services/api",
    ROOT / "apps/admin",
    ROOT / "apps/storefront",
    ROOT / "apps/mobile",
]


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


def serialize(values: dict[str, str]) -> str:
    return "\n".join(f"{key}={value}" for key, value in values.items()) + "\n"


def write_secure(path: Path, values: dict[str, str]) -> None:
    path.write_text(serialize(values))
    os.chmod(path, stat.S_IRUSR | stat.S_IWUSR)


def database_name(connection_string: str) -> str | None:
    match = re.search(r"(?i)(^|;)\s*Database\s*=\s*([^;]*)", connection_string)
    return match.group(2).strip() if match else None


def replace_database(connection_string: str, name: str) -> str:
    if re.search(r"(?i)(^|;)\s*Database\s*=", connection_string):
        return re.sub(r"(?i)(^|;)\s*Database\s*=\s*[^;]*", lambda m: f"{m.group(1)}Database={name}", connection_string)
    return f"{connection_string.rstrip(';')};Database={name}"


def profile_api(base: dict[str, str], profile: str) -> dict[str, str]:
    values = dict(base)
    values["HAULSCOUT_ENV"] = profile

    if profile == "production":
        values["ASPNETCORE_ENVIRONMENT"] = "Production"
        values["StorefrontCheckout__PublicApiBaseUrl"] = "https://scouthaul-production.up.railway.app"
        values.pop("StorefrontCheckout__PublicBaseUrlOverride", None)
        return values

    values["ASPNETCORE_ENVIRONMENT"] = "Development"
    values["ASPNETCORE_HTTP_PORTS"] = values.get("ASPNETCORE_HTTP_PORTS", "5080")
    values["Database__ApplyMigrationsOnStartup"] = "true"
    values["StorefrontCheckout__PublicApiBaseUrl"] = "http://localhost:5080"
    values["StorefrontCheckout__PublicBaseUrlOverride"] = "http://localhost:3000"

    connection = values.get("ConnectionStrings__Postgres")
    if connection:
        current_db = database_name(connection)
        target_db = "haulscout_dev" if profile == "development" else "haulscout_test"
        values["ConnectionStrings__Postgres"] = replace_database(connection, target_db if current_db else target_db)

    return values


def profile_web(base: dict[str, str], profile: str, app: str) -> dict[str, str]:
    values = dict(base)

    if app in {"admin", "storefront"}:
        if profile == "development":
            values["NEXT_PUBLIC_API_BASE_URL"] = "http://localhost:5080"
        elif profile == "test":
            values["NEXT_PUBLIC_API_BASE_URL"] = "https://scouthaul-test.up.railway.app"
        else:
            values["NEXT_PUBLIC_API_BASE_URL"] = "https://scouthaul-production.up.railway.app"

    if app == "mobile":
        if profile == "test":
            values["EXPO_PUBLIC_API_BASE_URL"] = "https://scouthaul-test.up.railway.app"
        elif profile == "production":
            values["EXPO_PUBLIC_API_BASE_URL"] = "https://scouthaul-production.up.railway.app"

    return values


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--write-production", action="store_true", help="also write .env.production.local from current values")
    args = parser.parse_args()

    for target in TARGETS:
        base = parse_env(target / ".env")
        if not base:
            continue

        app_name = target.name
        for profile in ("development", "test"):
            is_api = target.as_posix().endswith("/services/api")
            values = profile_api(base, profile) if is_api else profile_web(base, profile, app_name)
            write_secure(target / f".env.{profile}.local", values)

        if args.write_production:
            is_api = target.as_posix().endswith("/services/api")
            values = profile_api(base, "production") if is_api else profile_web(base, "production", app_name)
            write_secure(target / ".env.production.local", values)

    print("local profile files created; values were not printed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
