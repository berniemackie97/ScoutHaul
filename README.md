# HaulScout

HaulScout is a cross-platform resale workflow MVP for scouting, manual comps, deal analysis, inventory, sourcing trips, budgets, listings, sales, tasks, offline drafts, and sync queue foundations.

The app is intentionally not an AI app. It does not use paid AI services, marketplace scraping, Azure-only hosting, or fabricated pricing.

## Structure

```text
HaulScout/
  apps/mobile                 Expo React Native app
  services/api                ASP.NET Core Web API solution
  services/api/db/seed        External SQL seed scripts
  docs                        Product and architecture notes
  docker-compose.yml          PostgreSQL, API, and dev seed service
```

## Backend

Requirements:

- .NET 10 SDK
- Docker Desktop or compatible Docker daemon for containerized startup

Local configuration examples:

- `services/api/.env.example`
- `apps/mobile/.env.example`

Run locally with Docker:

```bash
cd /Volumes/EnderChest/Development/Repos/DotNet/Apps/HaulScout
docker compose up --build
```

Then open:

- API Swagger: `http://localhost:5080/swagger`
- Health: `http://localhost:5080/health`

`docker compose up` starts PostgreSQL on host port `5433`, runs EF Core migrations from the API, then runs `services/api/db/seed/development.sql` from a separate `db-seed` container. Seed data is not compiled into the EF model.

Run the API directly against the Docker database:

```bash
cd services/api/src/HaulScout.Api
dotnet run
```

Development appsettings point to `localhost:5433`. Production has no default database secret; set `ConnectionStrings__Postgres` from the runtime environment.

Run backend tests:

```bash
cd services/api
dotnet test HaulScout.slnx
```

Build all backend projects:

```bash
cd services/api
dotnet build HaulScout.slnx
```

Create a migration:

```bash
cd services/api
dotnet ef migrations add MigrationName \
  --project src/HaulScout.Infrastructure/HaulScout.Infrastructure.csproj \
  --startup-project src/HaulScout.Api/HaulScout.Api.csproj \
  --output-dir Persistence/Migrations
```

## Mobile

Requirements:

- Node.js
- Expo CLI through `npx`

Run:

```bash
cd /Volumes/EnderChest/Development/Repos/DotNet/Apps/HaulScout/apps/mobile
npm install
EXPO_PUBLIC_API_BASE_URL=http://localhost:5080 npx expo start
```

For Android emulator, use:

```bash
EXPO_PUBLIC_API_BASE_URL=http://10.0.2.2:5080 npx expo start
```

Type check:

```bash
npm run typecheck
```

Build a local Android APK for closed beta device testing:

```bash
cd /Volumes/EnderChest/Development/Repos/DotNet/Apps/HaulScout/apps/mobile
npm install
npx eas login
npx eas build:configure
npx eas build -p android --profile preview
```

The `preview` EAS profile is configured for an APK, so the resulting artifact can be installed directly on a test Android phone without publishing to Google Play. Use a LAN-accessible or hosted API URL in `EXPO_PUBLIC_API_BASE_URL`; `localhost` from a phone points at the phone, not your development machine.

For EAS cloud builds, set `EXPO_PUBLIC_API_BASE_URL`, `EXPO_PUBLIC_SUPABASE_URL`, `EXPO_PUBLIC_SUPABASE_ANON_KEY`, and `EAS_PROJECT_ID` in the matching EAS environment. EAS does not read your local `.env` file while resolving app config in cloud builds.

The mobile API base URL is read from `EXPO_PUBLIC_API_BASE_URL`. The API client strips trailing slashes, sends a real `Authorization: Bearer` token when a user is signed in, and only falls back to the MVP stub auth headers from `EXPO_PUBLIC_SCOUTHAUL_ORG_ID` and `EXPO_PUBLIC_SCOUTHAUL_USER_ID` when no session exists.

## CI/CD

GitHub Actions workflow: `.github/workflows/ci.yml`

The workflow runs backend restore, format, build, test, and NuGet audit; mobile install, Expo dependency check, TypeScript check, and npm audit; then Docker image build plus a Compose smoke test against `/health`, Swagger JSON, PostgreSQL, and the external seed script.

The API image publishes to GitHub Container Registry on `main`, version tags like `v1.0.0`, or a manual workflow dispatch with `publish_image=true`:

```text
ghcr.io/<owner>/<repo>/scouthaul-api
```

The Docker image includes the `org.opencontainers.image.source` label so GHCR can link the package back to the repository. Package visibility and access are controlled by GitHub package settings; keep the repository private and inherited package permissions enabled for private-by-default access.

## Auth

Development still supports stub auth for local smoke tests:

- `X-HaulScout-Organization-Id`
- `X-HaulScout-User-Id`

Real sign-in uses Supabase Auth for cross-platform mobile/web sessions. Configure the mobile app with your Supabase project values:

```bash
EXPO_PUBLIC_SUPABASE_URL=https://your-project.supabase.co
EXPO_PUBLIC_SUPABASE_ANON_KEY=your-publishable-or-anon-key
```

Configure the API to validate the same Supabase project:

```bash
Authentication__Supabase__Url=https://your-project.supabase.co
Authentication__Supabase__Audience=authenticated
# Optional only for legacy HS256 Supabase projects:
# Authentication__Supabase__JwtSecret=your-supabase-jwt-secret
```

Supabase owns authentication sessions. HaulScout owns organizations, teams, roles, permissions, inventory, routes, budgets, and billing state. When `StubAuth__DefaultOrganizationId` is present, development requests can still fall back to stub headers, but a valid Supabase bearer token takes precedence.

## Offline Rules

Offline mode stores local item drafts, scanned barcodes, asking prices, notes, manual estimates, queued inventory creates, queued comps, and sync operations in Expo SQLite.

Offline mode does not create live market recommendations. A deal analysis requires one of:

- online lookup/cached market data,
- internal sale history,
- synced sold comps,
- or a user-entered expected sale price.

## Marketplace Rules

HaulScout does not embed marketplace scraping or navigation flows in the app. Barcode scans run through configured product identity providers, then optional pricing providers such as PriceCharting, Keepa, SerpApi Google Shopping, and eBay Browse. When provider data is unavailable, HaulScout still generates external search links for eBay, Facebook Marketplace, Google, Google Shopping, Amazon, Whatnot, Mercari, and Craigslist so the user can capture manual comps.

Active listing prices are never treated as sold prices. Provider estimates are saved as `Estimate` observations and can be used as a cached fallback only when sold comps are unavailable. Every comp has a type such as `Sold`, `ActiveListing`, `ManualSold`, `ManualActive`, `InternalSale`, `Estimate`, or `Unknown`. Recommendations expose a pricing basis such as `LiveValidated`, `CachedRecent`, `CachedStale`, `ManualEstimate`, `InternalHistory`, or `Unavailable`.

## Manual QA Checklist

- Start Docker Compose and confirm `http://localhost:5080/health` returns healthy.
- Open Swagger in development and run dashboard, inventory list, market observation, market analysis, and budget summary endpoints.
- Start Expo with `EXPO_PUBLIC_API_BASE_URL=http://localhost:5080 npx expo start`.
- Scout an item, save a local draft, add it to inventory, capture a manual sold comp from the inventory detail screen, and run deal analysis.
- Verify deal analysis shows explicit pricing basis/confidence warnings and does not value an item from active listings only.
- Toggle offline mode or disconnect network, save a draft, queue sync, reconnect, and run Sync Now from Settings.
- Upload inventory photos, reload the item, and confirm remote photos render on another device signed into the same workspace.
- Record a current or historical sale from mobile or admin with governed channel, tax, fee, and cost inputs; then pack, ship, deliver, cancel, return, and refund it through the shared audited workflow.
- Delete routes/stops/listings/tasks/budget expenses from the mobile UI and confirm the list updates without stale rows.

## Current Verification

Validated locally:

```bash
cd services/api && dotnet build HaulScout.slnx
cd services/api && dotnet test HaulScout.slnx
cd services/api && dotnet format HaulScout.slnx --verify-no-changes --verbosity minimal
cd services/api && dotnet list HaulScout.slnx package --vulnerable --include-transitive
cd apps/mobile && npm run typecheck
cd apps/mobile && npx expo-doctor@latest
cd apps/mobile && npm audit --audit-level=high
docker build -f services/api/src/HaulScout.Api/Dockerfile services/api
docker compose config
docker compose up -d --build
curl -fsS http://localhost:5080/health
curl -fsS http://localhost:5080/swagger/v1/swagger.json
EXPO_PUBLIC_API_BASE_URL=http://localhost:5080 npx expo start --localhost --port 8081
```

`npm install --package-lock-only` was also run after package metadata cleanup. The Expo startup check was bounded and then stopped after Metro reached `http://localhost:8081`. `npm audit` currently reports four moderate npm advisories; they were not force-fixed because the suggested path can introduce breaking dependency updates.
