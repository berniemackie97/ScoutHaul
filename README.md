# ScoutHaul

ScoutHaul is a cross-platform resale workflow MVP for scouting, manual comps, deal analysis, inventory, sourcing trips, budgets, listings, sales, tasks, offline drafts, and sync queue foundations.

The app is intentionally not an AI app. It does not use paid AI services, marketplace scraping, Azure-only hosting, or fabricated pricing.

## Structure

```text
ScoutHaul/
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
cd /Volumes/EnderChest/Development/Repos/DotNet/Apps/ScoutHaul
docker compose up --build
```

Then open:

- API Swagger: `http://localhost:5080/swagger`
- Health: `http://localhost:5080/health`

`docker compose up` starts PostgreSQL on host port `5433`, runs EF Core migrations from the API, then runs `services/api/db/seed/development.sql` from a separate `db-seed` container. Seed data is not compiled into the EF model.

Run the API directly against the Docker database:

```bash
cd services/api/src/ScoutHaul.Api
dotnet run
```

Development appsettings point to `localhost:5433`. Production has no default database secret; set `ConnectionStrings__Postgres` from the runtime environment.

Run backend tests:

```bash
cd services/api
dotnet test ScoutHaul.slnx
```

Build all backend projects:

```bash
cd services/api
dotnet build ScoutHaul.slnx
```

Create a migration:

```bash
cd services/api
dotnet ef migrations add MigrationName \
  --project src/ScoutHaul.Infrastructure/ScoutHaul.Infrastructure.csproj \
  --startup-project src/ScoutHaul.Api/ScoutHaul.Api.csproj \
  --output-dir Persistence/Migrations
```

## Mobile

Requirements:

- Node.js
- Expo CLI through `npx`

Run:

```bash
cd /Volumes/EnderChest/Development/Repos/DotNet/Apps/ScoutHaul/apps/mobile
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

The mobile API base URL is read from `EXPO_PUBLIC_API_BASE_URL`. The API client strips trailing slashes and sends the MVP stub auth headers from `EXPO_PUBLIC_SCOUTHAUL_ORG_ID` and `EXPO_PUBLIC_SCOUTHAUL_USER_ID`.

## CI/CD

GitHub Actions workflow: `.github/workflows/ci.yml`

The workflow runs backend restore, format, build, test, and NuGet audit; mobile install, Expo dependency check, TypeScript check, and npm audit; then Docker image build plus a Compose smoke test against `/health`, Swagger JSON, PostgreSQL, and the external seed script.

The API image publishes to GitHub Container Registry on `main`, version tags like `v1.0.0`, or a manual workflow dispatch with `publish_image=true`:

```text
ghcr.io/<owner>/<repo>/scouthaul-api
```

The Docker image includes the `org.opencontainers.image.source` label so GHCR can link the package back to the repository. Package visibility and access are controlled by GitHub package settings; keep the repository private and inherited package permissions enabled for private-by-default access.

## MVP Auth

Authentication is stubbed for the MVP. The mobile app sends:

- `X-ScoutHaul-Organization-Id`
- `X-ScoutHaul-User-Id`

The API isolates this in `ICurrentUserContext` so real authentication can replace it without rewriting controllers.

## Offline Rules

Offline mode stores local item drafts, scanned barcodes, asking prices, notes, manual estimates, queued inventory creates, queued comps, and sync operations in Expo SQLite.

Offline mode does not create live market recommendations. A deal analysis requires one of:

- online lookup/cached market data,
- internal sale history,
- synced sold comps,
- or a user-entered expected sale price.

## Marketplace Rules

Marketplace scraping is not part of the MVP. ScoutHaul generates external search links for eBay, Facebook Marketplace, Google, Google Shopping, Amazon, Whatnot, Mercari, and Craigslist.

Active listing prices are never treated as sold prices. Every comp has a type such as `Sold`, `ActiveListing`, `ManualSold`, `ManualActive`, `InternalSale`, `Estimate`, or `Unknown`. Recommendations expose a pricing basis such as `LiveValidated`, `CachedRecent`, `CachedStale`, `ManualEstimate`, `InternalHistory`, or `Unavailable`.

## Manual QA Checklist

- Start Docker Compose and confirm `http://localhost:5080/health` returns healthy.
- Open Swagger in development and run dashboard, inventory list, market observation, market analysis, and budget summary endpoints.
- Start Expo with `EXPO_PUBLIC_API_BASE_URL=http://localhost:5080 npx expo start`.
- Scout an item, save a local draft, add it to inventory, capture a manual sold comp from the inventory detail screen, and run deal analysis.
- Verify deal analysis shows explicit pricing basis/confidence warnings and does not value an item from active listings only.
- Toggle offline mode or disconnect network, save a draft, queue sync, reconnect, and run Sync Now from Settings.
- Record a listing and sale from API/Swagger until the mobile sale flow is built out, then confirm dashboard totals update.

## Current Verification

Validated locally:

```bash
cd services/api && dotnet build ScoutHaul.slnx
cd services/api && dotnet test ScoutHaul.slnx
cd services/api && dotnet format ScoutHaul.slnx --verify-no-changes --verbosity minimal
cd services/api && dotnet list ScoutHaul.slnx package --vulnerable --include-transitive
cd apps/mobile && npm run typecheck
cd apps/mobile && npx expo install --check
cd apps/mobile && npm audit --audit-level=high
docker compose config
docker compose up -d --build
curl -fsS http://localhost:5080/health
curl -fsS http://localhost:5080/swagger/v1/swagger.json
EXPO_PUBLIC_API_BASE_URL=http://localhost:5080 npx expo start --localhost --port 8081
```

`npm install --package-lock-only` was also run after package metadata cleanup. The Expo startup check was bounded and then stopped after Metro reached `http://localhost:8081`. `npm audit` currently reports four moderate npm advisories; they were not force-fixed because the suggested path can introduce breaking dependency updates.
