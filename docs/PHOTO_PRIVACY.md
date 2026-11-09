# Inventory photo privacy

## Delivery and access contract

Inventory photos may include private operator, customer-owned, consignment or storage-location imagery. A GUID is an address, not authorization.

| Request | Audience | Gate | Cache |
| --- | --- | --- | --- |
| `GET /api/inventory/{itemId}/photos/{photoId}/thumbnail` | Operator | Authenticated `ManageInventory` permission and exact organization/item/photo match | No-store |
| `GET /api/inventory/{itemId}/photos/{photoId}/original` | Operator | Same | No-store |
| `GET /api/storefront/photos/{photoId}` | Shopper | Photo belongs to an item with a public storefront listing in the same organization and an enabled store domain | No-store |

The public route is independent of the operator inventory route. It does not authorize unlisted items, and access is recalculated on every request instead of caching published images as immutable. Active, reserved and sold product detail pages remain public per the existing catalog contract; draft/archived listings do not expose photos.

## Operator surfaces

- The admin converts API inventory-photo asset paths into a same-origin Next.js route. That route retrieves the current Supabase operator session server-side and forwards only validated item/photo IDs and image sizes to the authenticated API. The bearer token is not placed in HTML, image addresses or query strings. Upstream errors are returned without response payloads.
- Mobile uses an authenticated image source only for the configured API origin plus inventory-photo URL pattern. It adds the existing session bearer and active organization header. Third-party and public storefront images never receive credentials. Private image responses are not persisted in the Expo image cache.
- Historical inventory API URLs remain in application DTOs; only presentation loading changes. Product storefront URLs remain unchanged.

## New uploads and previously stored images

The upload processor decodes and auto-orients JPEG, PNG and WebP, then always re-encodes originals and thumbnails using encoders that omit metadata. The original extension format is preserved unless the existing imported-photo resize rule converts it to JPEG; transparent PNG pixels remain supported. This prevents new uploads from passing GPS EXIF and related metadata through as original bytes.

**Not yet complete:** historical originals may still contain EXIF/XMP/GPS until the explicit repair command below has been executed, audited and its failures resolved. Publicly cached or previously downloaded copies cannot be recalled. New-upload sanitation alone is not acceptance of historical privacy.

## Historical-photo repair

The API no longer serves its web root. This blocks direct access to any old `/uploads/inventory/...` files through the API even if deployed volumes still contain them; Swagger's own development UI continues through its mapped middleware. Existing references to those legacy file paths may stop displaying and need a separate inventory audit, migration or removal.

A manual metadata-repair command uses the same image decoder and metadata-free encoders as new uploads. It visits all organizations (including unpublished items) by photo ID, re-encodes the original and thumbnail, updates dimensions/content type/sizes, and saves **one photo at a time**. Database blobs and configured object-storage blobs use the same storage abstraction; image IDs remain stable. The command is never scheduled or run automatically.

Before changing any images:

1. Back up the PostgreSQL database **and** the object bucket, including keys and versions. Verify that you can restore both.
2. Take a maintenance window: stop API writes, marketplace photo sync, and the existing object-storage backfill worker on **all instances**. Do not run a second metadata repair process concurrently.
3. Use the environment's normal API database and object-storage credentials. Ensure the schema is up to date. Run a read-only preview:

```bash
dotnet run --project services/api/src/HaulScout.Api -- repair-photo-metadata --dry-run 10
```

To process the first batch deliberately:

```bash
dotnet run --project services/api/src/HaulScout.Api -- repair-photo-metadata --apply 10
```

Each successfully committed photo emits its ID to the operator log. The final line includes `Scanned`, `Reencoded`, and `NextAfter`. Repeat with the **last successfully saved photo ID**:

```bash
dotnet run --project services/api/src/HaulScout.Api -- repair-photo-metadata --apply 10 <last-saved-photo-id-N>
```

`--dry-run` never saves bytes or metadata and its cursor **must not** be used to resume `--apply`. A batch with `Scanned=0` has reached the end of the current photo-ID range. The limit is 1–10, bounding tracked photos and in-memory encoded copies. Keep a durable maintenance record of the applied cursor and check resulting bytes. Do not rerun successfully processed ranges casually: lossy encodings would be re-encoded.

**Failure policy:** A missing/corrupted/unsupported/oversize original or a bucket/database error aborts the pass at that photo. Already committed photos remain fixed; resume from the last success after fixing or restoring the failed record. Never skip an image without recording an explicit incident and disposition. If object storage accepts a write but the row save fails, retrying the same photo is supported; inspect the two object keys and row afterward. A changed media extension can leave an old, unreferenced object that needs separate cleanup after verification.

The command does **not** reach external CDN/Next.js caches or legacy web-root files and cannot revoke previously downloaded copies. It does not change links that reference obsolete `/uploads/inventory` paths. Inspect deployed mounts, old URLs and media caches before closing #283.

## Deployment and acceptance

The admin and mobile readers must be deployed together with the API authorization change. A backend-only deploy would make existing image tags for private items stop working; verify cross-surface rendering in staging before production.

- [ ] .NET tests run: owner can read thumbnail/original, foreign organization gets no bytes, unauthenticated inventory path gets 401, storefront public listed photo remains available, private item is 404.
- [ ] Image tests run with actual GPS-tagged JPEG and transparent PNG; confirm metadata absent from stored original/thumbnail and orientation preserved.
- [ ] Mobile Jest, type-check, lint and device preview after sign-in/token refresh/organization switch; sign-out cannot show a cached private image.
- [ ] Admin type-check/build and private-photo previews in inventory table, picker and detail, plus proper 401 after sign-out.
- [ ] Public product images stay accessible without tokens, and disappear after listing visibility is revoked; verify caches and hosting image proxies.
- [ ] Execute and audit metadata repair across database/object-storage photos in maintenance windows, resolve failures, inventory legacy web-root files/links and verify the output bytes.
- [ ] Review PR diff, run CI, verify deployment sequencing and capture staging evidence before closing #283.

Parent issue: [#283](https://github.com/berniemackie97/ScoutHaul/issues/283). Follow-on storage/upload resilience is [#284](https://github.com/berniemackie97/ScoutHaul/issues/284).
