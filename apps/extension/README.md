# HaulScout Sync (browser extension)

Keeps HaulScout in step with your marketplace accounts, from the browser you already sell in. It
reads **your own** seller pages in your own signed-in browser: sales, their fees, and live listings.
It sends what it reads to HaulScout, which decides what it means. Design:
[docs/CHANNEL_SYNC_DESIGN.md](../../docs/CHANNEL_SYNC_DESIGN.md).

Firefox is the first target. The same code builds for Chrome and Edge, and Safari is planned.

## What it does, and what it never does

- **Reads only.** Adapters read the seller's own data: the order list, the transaction ledger and
  the active listings. They never share, follow, bump, make offers, or change listings.
- **Only when switched on.** Nothing runs on a schedule until someone turns a schedule on in
  HaulScout under **Sync**. Every scheduled scan is off by default. A scan can always be run by hand
  from HaulScout or from this popup.
- **Only as the right account.** Each HaulScout channel account is bound to one marketplace user.
  A browser signed in as anyone else is refused before anything is read.
- **Gently.** A safety governor spaces requests with random jitter and caps them per minute and per
  day. It backs off when a marketplace pushes back: a rate limit, a security check, or pages that no
  longer read as expected. HaulScout can switch any marketplace off for every browser at once.

## How it works

| Piece | Where |
|---|---|
| Background agent: checks in every minute, runs due work one item at a time, notices a sign-in | `src/entrypoints/background.ts`, `src/agent/agent.ts`, `src/agent/runner.ts` |
| Safety governor, as pure functions | `src/agent/governor.ts` |
| The sync tab, and waiting for a page to really load | `src/agent/transport.ts` |
| eBay adapter: reads the JSON each Seller Hub page embeds | `src/adapters/ebay/` |
| Popup: connect, marketplace access, what is happening, scan now, activity | `src/popup/` (states worked out in `model.ts`) |
| Portal bridge: tells the HaulScout portal the extension is here and can be woken | `src/entrypoints/portal.content.ts` |

**Why it reads through a marketplace tab.** It reads inside a background tab on the marketplace's
own site, not from the extension's background. Firefox keeps each site's cookies separate, so a
background request would not reliably carry your session.

**Why pages are opened rather than fetched.** Seller pages are opened in that tab the way a click
opens them, then read once loaded. A page fetched from script sends different request headers from a
person viewing it, and marketplaces' bot protection notices. eBay showed its security check to
exactly that during development. Only the small JSON endpoints the site's own pages call are
requested from the page.

**The sync tab.** Nobody has to open or keep a marketplace page open. When there is work, the
extension opens its own tab: pinned, in the background, and in an ordinary window, never a private
one. Work that follows reuses it, and it closes itself a few seconds after the last piece. Closing
it by hand mid-scan only fails that scan, with a reason; the next scan opens another. A quick
sign-in check uses an eBay tab you already have open when there is one, and never navigates it.

**Waiting for a page.** A page is read only once it has finished loading at a marketplace address,
and, after a navigation, only once that navigation has begun. Firefox reports a brand-new tab as
"complete" while it still shows about:blank, before the real page even starts, and reading it then
fails with "Missing host permission for the tab". `judgeTab` in `transport.ts` holds this rule, and
its tests replay Firefox's sequence.

**Needing a person.** Some trouble only a person can clear: signed out, signed in as another
account, access not allowed, or a security check. The extension says so in the popup and on its
heartbeat. HaulScout then holds the work back, and the portal shows what is needed. A scan someone
asked for waits and runs as soon as it can, instead of failing.

The extension notices when that happens without anyone pressing a button. When an eBay page
finishes loading in one of your tabs, it looks again: you signed in, or you finished the check in
Seller Hub.

**Pairing** uses a one-time code:
1. HaulScout shows the code under **Sync → Browsers → Connect a browser**.
2. You paste it into this popup.
3. The extension gets a device token that can only sync. It rotates weekly, and you can revoke it
   per browser from HaulScout.

Pairing is deliberately done in the popup, never by a web page. A script on a page could otherwise
connect your browser to someone else's organization.

## Develop

```bash
pnpm --filter @overhaul/extension dev          # Firefox, against the local API (localhost:5080) and portal (localhost:3001)
pnpm --filter @overhaul/extension test
pnpm --filter @overhaul/extension build        # Firefox, production → .output/firefox-mv3
pnpm --filter @overhaul/extension build:chrome # Chrome/Edge, production → .output/chrome-mv3
```

To point a build at another API, set `WXT_API_ORIGIN`. For the portal, set `WXT_PORTAL_ORIGINS`
to every address it is reached at, comma-separated. The bridge runs only there, so a portal opened
anywhere else cannot wake the extension, and its scans wait for the next heartbeat. Production
defaults to the portal's Vercel address.

**Load a build in Firefox for testing:** open `about:debugging#/runtime/this-firefox`, choose
**Load Temporary Add-on…**, and pick `.output/firefox-mv3/manifest.json`. A temporary add-on is
removed when Firefox restarts.

**Keep it installed:** a release build must be signed by Mozilla.
1. Package it with `pnpm --filter @overhaul/extension zip`.
2. Sign it as an **unlisted** add-on with `web-ext sign --channel unlisted` or on addons.mozilla.org.
   Signing needs the owner's AMO API credentials.
3. Install the signed `.xpi`.

## Adding a marketplace

1. Write an adapter against the real site, in a browser signed in to that marketplace. Its
   `readable` and `pageKind` tell the sync tab which pages are the marketplace's own, and which are
   its sign-in form and security check.
2. List it in `src/adapters/registry.ts` and add its origin to `marketplaceOrigins` in
   `wxt.config.ts`.
3. List it in `SyncPlatforms` on the server, in the same change.

The Depop adapter must build the same sale key as the server's Depop report parser, so a scan and
an export of the same sale land as one order.
