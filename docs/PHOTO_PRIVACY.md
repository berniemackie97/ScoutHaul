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

**Not yet complete:** previously stored originals may still contain EXIF/XMP/GPS or other metadata, including objects already served or cached externally before this change. An audited in-place remediation is still required for both database and object-storage photos. Preserve photo IDs and references, verify the new bytes, update the recorded media/size/dimensions, then replace old objects with bounded batches, resumability and failure reporting. Never declare the old set scrubbed merely because this upload path is fixed. Public storefront originals must not be considered fully remediated until historic bytes are processed.

## Deployment and acceptance

The admin and mobile readers must be deployed together with the API authorization change. A backend-only deploy would make existing image tags for private items stop working; verify cross-surface rendering in staging before production.

- [ ] .NET tests run: owner can read thumbnail/original, foreign organization gets no bytes, unauthenticated inventory path gets 401, storefront public listed photo remains available, private item is 404.
- [ ] Image tests run with actual GPS-tagged JPEG and transparent PNG; confirm metadata absent from stored original/thumbnail and orientation preserved.
- [ ] Mobile Jest, type-check, lint and device preview after sign-in/token refresh/organization switch; sign-out cannot show a cached private image.
- [ ] Admin type-check/build and private-photo previews in inventory table, picker and detail, plus proper 401 after sign-out.
- [ ] Public product images stay accessible without tokens, and disappear after listing visibility is revoked; verify caches and hosting image proxies.
- [ ] Remediate pre-existing photos (database and object storage), including corrupt-item handling, batch restart and audit of output bytes.
- [ ] Review PR diff, run CI, verify deployment sequencing and capture staging evidence before closing #283.

Parent issue: [#283](https://github.com/berniemackie97/ScoutHaul/issues/283). Follow-on storage/upload resilience is [#284](https://github.com/berniemackie97/ScoutHaul/issues/284).
