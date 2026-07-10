# HaulScout Listing Helper (browser extension)

Human-led listing prefill and channel tracking for HaulScout.

**Compliance model (non-negotiable):** the extension only acts inside *your own logged-in* marketplace tabs, and only on an explicit click. It **prefills** listing forms so you review and submit — it never auto-submits, never runs headless, never bulk-acts without you. This is the pattern marketplaces allow.

## What it does today (v0.1 scaffold)
- **Connects** to your HaulScout API with your session token (Settings in the popup).
- **Reads ready inventory + channels** from the API.
- **Active eBay tab → HaulScout:** read the current eBay listing (title, price, item id, URL) and save it as an `Active` ChannelListing against the matched item + eBay channel account.
- **HaulScout → eBay sell form:** prefill title / description / price on the open eBay sell page for you to review and submit.

## Architecture
- `manifest.json` — MV3. Host permissions: HaulScout API + `*.ebay.com`.
- `api.js` — thin API client (Bearer token from `chrome.storage`). No business logic, **no AI**.
- `popup.html` / `popup.js` — the operator UI.
- `content-ebay.js` — reads / prefills the eBay page. **No submit.**
- `background.js` — thin message hub.

The brain lives in the API (`ChannelAccount`, `ChannelListing`); the extension is the hands. It does not auto-submit, auto-list, auto-delist, bulk scrape, or run unattended marketplace workflows.

## Load it (dev)
1. Chrome → `chrome://extensions` → enable Developer mode.
2. **Load unpacked** → select this `apps/extension` folder.
3. Open the popup → Settings → paste your API base + token → Save.

## Roadmap
- eBay: robust field mapping and optional image prefill where the marketplace form allows it.
- Then Poshmark, Depop, Vinted adapters (DOM-driven, same human-led rules).
- Listing templates, per-channel price guidance, analytics.
- Proper auth (OAuth/session bridge) to replace token paste; packaging + icons.
