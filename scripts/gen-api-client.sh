#!/usr/bin/env bash
# Regenerates the typed TS API client from the .NET API's OpenAPI document.
#   1. build the API
#   2. emit packages/api-client/openapi.json via the Swashbuckle CLI (no DB needed)
#   3. run openapi-typescript -> packages/api-client/src/schema.d.ts
#
# Run from anywhere: ./scripts/gen-api-client.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_DIR="$ROOT/services/api"
DLL="src/HaulScout.Api/bin/Debug/net10.0/HaulScout.Api.dll"

echo "==> Building API"
dotnet build "$API_DIR/src/HaulScout.Api/HaulScout.Api.csproj" -v q --nologo

echo "==> Emitting OpenAPI document"
cd "$API_DIR"
# Dummy config so the host builds without a database; migrations-on-startup disabled.
ConnectionStrings__Postgres="Host=localhost;Database=x;Username=x;Password=x" \
Authentication__Supabase__Url="https://example.supabase.co" \
Database__ApplyMigrationsOnStartup=false \
ASPNETCORE_ENVIRONMENT=Development \
dotnet swagger tofile --output "$ROOT/packages/api-client/openapi.json" "$DLL" v1

echo "==> Generating TypeScript types"
cd "$ROOT"
pnpm --filter @overhaul/api-client generate

echo "==> Done. API client regenerated."
