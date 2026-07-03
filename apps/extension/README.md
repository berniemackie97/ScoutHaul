# HaulScout Cross-Lister (browser extension)

Human-led cross-listing + inventory sync, in the style of List Perfectly / Vendoo — integrated with the HaulScout API instead of a third party.

**Compliance model (non-negotiable):** the extension only acts inside *your own logged-in* marketplace tabs, and only on an explicit click. It **prefills** listing forms so you review and submit — it never auto-submits, never runs headless, never bulk-acts without you. This is the pattern marketplaces allow.

## What it does today (v0.1 scaffold)
- **Connects** to your HaulScout API with your session token (Settings in the popup).
- **Reads inventory + channels** from the API.
- **Active eBay tab → HaulScout:** read the current eBay listing (title, price, item id, URL) and save it as an `Active` ChannelListing against the matched item + eBay channel account.
- **HaulScout → eBay sell form:** prefill title / description / price on the open eBay sell page for you to review and submit.

## Architecture
- `manifest.json` — MV3. Host permissions: HaulScout API + `*.ebay.com`.
- `api.js` — thin API client (Bearer token from `chrome.storage`). No business logic, **no AI**.
- `popup.html` / `popup.js` — the operator UI.
- `content-ebay.js` — reads / prefills the eBay page. **No submit.**
- `background.js` — thin message hub (reserved for future delist-on-sale pushes).

The brain lives in the API (`ChannelAccount`, `ChannelListing`); the extension is the hands.

## Load it (dev)
1. Chrome → `chrome://extensions` → enable Developer mode.
2. **Load unpacked** → select this `apps/extension` folder.
3. Open the popup → Settings → paste your API base + token → Save.

## Roadmap
- eBay: robust field mapping, image push, delist-on-sale round-trip.
- Then Poshmark, Depop, Vinted adapters (DOM-driven, same human-led rules).
- Image reconciliation: published images win → overwrite HaulScout photos.
- Bulk cross-post, templates, per-channel price rules, analytics.
- Proper auth (OAuth/session bridge) to replace token paste; packaging + icons.
