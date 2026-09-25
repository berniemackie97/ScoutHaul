# ScoutHaul environments

Railway is the source of truth for hosted API/runtime secrets.

## Hosted environments

| Environment | API URL | Database | Stripe mode |
| --- | --- | --- | --- |
| production | `https://scouthaul-production.up.railway.app` | Railway production Postgres volume | live |
| development | `https://scouthaul-development.up.railway.app` | Railway development Postgres volume | test |
| test | `https://scouthaul-test.up.railway.app` | Railway test Postgres volume | test |

Never share a Postgres connection string across these Railway environments.

## Required production Stripe setup

Production requires both values, from the same Stripe mode:

- `Stripe__ApiKey`: live server/restricted key, not publishable key.
- `Stripe__WebhookSecret`: signing secret for the live webhook endpoint.

Webhook endpoint:

```text
https://scouthaul-production.up.railway.app/api/storefront/stripe/webhook
```

Required live events:

- `checkout.session.completed`
- `checkout.session.expired`

The storefront does not need a Stripe publishable key because checkout uses Stripe-hosted Checkout Sessions.

## Photo storage

Photo images are kept in the database until an object storage bucket is configured, and in the bucket after that.
Both are read through the same port, and reads fall back to the database, so turning it on is a setting rather than
a migration: existing photos keep serving while a background worker moves them across.

| Variable | Example | Notes |
| --- | --- | --- |
| `Storage__Photos__ObjectStorage__Bucket` | `haulscout-photos` | Setting this is what enables object storage. |
| `Storage__Photos__ObjectStorage__ServiceUrl` | `https://<account-id>.r2.cloudflarestorage.com` | Must be https. |
| `Storage__Photos__ObjectStorage__AccessKeyId` | | From the bucket's own API token. |
| `Storage__Photos__ObjectStorage__SecretAccessKey` | | Never leaves Railway. |
| `Storage__Photos__ObjectStorage__Region` | `auto` | Optional; `auto` is the default and is what R2 wants. |
| `Storage__Photos__ObjectStorage__Prefix` | `production` | Optional; lets one bucket hold more than one environment. |

Set the credentials **before** the bucket. A bucket configured without credentials is refused at startup, on purpose
— a deployment that cannot store photos should fail loudly rather than write them somewhere unintended.

The bucket stays private. Images are served by the API from its existing photo routes, so nothing in the storefront
or the admin app changes, and a bucket that is never public cannot leak one organization's photos to another.

Moving existing photos is done by the backfill worker, which stops for good once the backlog is empty
(`Storage__Photos__Backfill__Enabled` turns it off). The space those images occupied is not returned to the volume
until the table is rewritten, which is an operator's choice of moment:

```sql
VACUUM FULL "InventoryPhotos";
```

## Local profiles

Profile files are gitignored and should stay local:

- `services/api/.env.development.local`
- `services/api/.env.test.local`
- `services/api/.env.production.local`
- same pattern for `apps/admin`, `apps/storefront`, and `apps/mobile`

Switch all local app `.env` files:

```bash
scripts/env/use-local-profile.sh development
scripts/env/use-local-profile.sh test
scripts/env/use-local-profile.sh production --allow-production
```

Regenerate local profile files from current local `.env` files:

```bash
scripts/env/bootstrap-local-profiles.py --write-production
```

Create/reconcile hosted Railway envs, DB bindings, and checkout defaults:

```bash
scripts/railway/sync-railway-environments.py --ensure-railway-envs
```

Safely write the local API production profile from Railway production:

```bash
scripts/railway/sync-railway-environments.py --write-local-production-api-env
```

Create/reconcile the live Stripe webhook and set Railway production `Stripe__WebhookSecret`:

```bash
scripts/railway/sync-railway-environments.py --sync-production-stripe --production-live-key-from-railway
```

## Rules

- Use `Stripe__ApiKey`, not `Stripe__LiveApiKey`; the app only binds `Stripe__ApiKey`.
- Use the matching webhook secret for the same Stripe mode.
- Production local profile is allowed, but it is dangerous; activate it only when intentionally testing against live systems.
- Do not commit `.env`, `.env.*`, CSV exports, or dashboard screenshots with secrets/PII.

References:

- [Railway environments](https://docs.railway.com/environments)
- [Railway environment CLI](https://docs.railway.com/cli/environment)
- [Railway variables](https://docs.railway.com/variables)
