# Browser extension security boundaries

## Data and trust boundaries

The browser extension reads seller pages in the operator’s authenticated marketplace session, then sends order, fee, listing and buyer-related data to the ScoutHaul API. The merchant page and its embedded HTML/JSON are **untrusted input**; the extension does not inherit the page’s permission to instruct its background agent.

| Boundary | Asset | Enforcement |
| --- | --- | --- |
| Popup → background | Pairing code, account selection, manual scan | Extension-origin sender check plus runtime command validation; only known popup actions and bounded parameters are accepted |
| Admin portal → content script → background | Connected status and wake signal | Build-time portal origin matching, window message origin/source checks and background sender check; status/wake only |
| Marketplace tab → extension | Seller session, order details, listing HTML and embedded JSON | Optional host access, platform parsing, agent governor and transport destination checks; source URLs are never authority to navigate arbitrarily |
| Extension → ScoutHaul API | Device token, orders and buyer information | HTTPS API origin fixed at build time, device-token authentication and organization membership checked by the API |
| Browser local storage | Device token and scan progress | `storage.local` rather than `storage.sync`; token never sent through portal bridge or popup messages |

## Permission inventory

- `storage`: pairing token, scan state, account sessions and governor decisions, retained on the local device.
- `alarms`: background heartbeat without a permanently running process.
- `scripting`: read selected seller pages and the marketplace’s own read-only endpoints inside marketplace tabs.
- `cookies`: marketplace session checks through `browser.cookies.get` for a known adapter home and cookie name. Cookie values are not sent through bridge messages.
- Optional `https://*.ebay.com/*`, `https://*.vinted.com/*`, `https://*.poshmark.com/*`, `https://*.depop.com/*`: explicitly granted seller-site access, not universal browsing permission.

The transport only navigates to the adapter’s configured HTTPS home origin. Reads requested from that origin are constrained to that origin, except eBay’s listing-description document on a verified `ebaydesc.com` hostname. Imported iframe addresses must not expand that allow-list. A hostile host suffix, credentials in the URL, HTTP URL, and relative URL are refused before script execution. Browsers may follow redirects inside a page request; redirected content is not a reason to widen the initial allow-list.

## Credential and failure handling

Pairing is popup-only with a one-time code. The server issues a scoped browser token and checks membership and revocation on authenticated agent requests. A heartbeat can rotate the token; the stored heartbeat removes the replacement token after rotating the local pairing. On successful disconnect, local credentials and scan state are removed. If remote revocation fails, local credentials remain so the operator can retry rather than falsely being shown as disconnected. A server-confirmed expired or revoked token is cleared.

Marketplace request errors and browser exceptions are not logged verbatim; unexpected failures are classified without serializing potentially sensitive URLs, response bodies, cookies or tokens. Operational messages may still contain marketplace name, error kind, and non-sensitive status. The portal bridge never returns a device token.

## Threat model and verification

Attacker-controlled inputs include imported listing HTML, marketplace responses, URL attributes, malformed extension messages and other web pages attempting to impersonate the admin portal. Security properties to preserve:

- A listing description cannot cause an extension-privileged request to an arbitrary host.
- A web page cannot pair/disconnect a browser or request inventory scans through the portal bridge.
- A failed remote revoke cannot silently discard a still-valid local credential.
- Error messages and logs do not leak raw request payloads or extension credentials.
- Host permissions are granted for seller sites only, and sales scans remain read-only.

Regression coverage lives in `src/agent/messages.test.ts`, `src/agent/transport.test.ts`, `src/agent/agent.test.ts` and `src/adapters/ebay/specifics.test.ts`. Live Firefox and Chromium permission prompts, token rotation/expiry with a staging API, hostile DOM fixtures for every marketplace and CI execution must still be verified before #287 can close. This document is a source audit and threat model, not evidence of live-browser certification.
