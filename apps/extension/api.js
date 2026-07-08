// Thin client to the HaulScout API. The extension never contains business logic or AI —
// it reads inventory + channel state and reports listing state back. Auth is the operator's
// own API session token, stored locally and sent as a Bearer header.

const DEFAULT_BASE = "https://scouthaul-production.up.railway.app";

export async function getConfig() {
  const stored = await chrome.storage.local.get(["apiBase", "token"]);
  return { apiBase: stored.apiBase || DEFAULT_BASE, token: stored.token || "" };
}

export async function setConfig({ apiBase, token }) {
  await chrome.storage.local.set({ apiBase: (apiBase || DEFAULT_BASE).replace(/\/+$/, ""), token: token || "" });
}

async function apiFetch(path, { method = "GET", body } = {}) {
  const { apiBase, token } = await getConfig();
  if (!token) throw new Error("Not connected. Add your HaulScout API token in settings.");
  const response = await fetch(`${apiBase}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(body ? { "Content-Type": "application/json" } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (response.status === 401) throw new Error("Session expired. Update your token in settings.");
  if (!response.ok) throw new Error(`API ${response.status}: ${(await response.text()).slice(0, 200)}`);
  return response.status === 204 ? null : response.json();
}

export const api = {
  inventory: () => apiFetch("/api/inventory?includeArchived=false"),
  channelAccounts: () => apiFetch("/api/channel-accounts"),
  listingsForItem: (itemId) => apiFetch(`/api/inventory/${itemId}/channel-listings`),
  upsertListing: (itemId, body) =>
    apiFetch(`/api/inventory/${itemId}/channel-listings`, { method: "PUT", body }),
  worklist: (status) => apiFetch(`/api/channel-listings${status ? `?status=${encodeURIComponent(status)}` : ""}`),
  // Push scraped marketplace orders into the idempotent finance landing zone. `platform` is the channel these
  // orders came from (e.g. "eBay"); re-pushing the same order id updates it in place, never duplicates.
  ingestPlatformOrders: (platform, orders) =>
    apiFetch("/api/platform-orders/ingest", { method: "POST", body: { platform, orders } }),
  platformOrders: (status) =>
    apiFetch(`/api/platform-orders${status ? `?status=${encodeURIComponent(status)}` : ""}`),
};
