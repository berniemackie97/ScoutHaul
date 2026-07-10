import { api, getConfig, setConfig } from "./api.js";

const el = (id) => document.getElementById(id);
const state = { items: [], accounts: [], capturedListing: null };

function money(value) {
  return typeof value === "number" ? `$${value.toFixed(2)}` : "—";
}

async function activeTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  return tab;
}

async function connect() {
  el("status").textContent = "Loading…";
  el("status").className = "muted";
  try {
    const [items, accounts] = await Promise.all([api.inventory(), api.channelAccounts()]);
    state.items = Array.isArray(items) ? items : [];
    state.accounts = (Array.isArray(accounts) ? accounts : []).filter((a) => a.isActive);
    renderList();
    fillMatchers();
    el("ebayCard").classList.remove("hidden");
    el("status").textContent = `${state.items.length} items · ${state.accounts.length} channels`;
  } catch (error) {
    el("status").textContent = String(error.message || error);
    el("status").className = "err";
    el("settings").open = true;
  }
}

function renderList() {
  const container = el("list");
  container.innerHTML = "";
  for (const item of state.items) {
    const div = document.createElement("div");
    div.className = "item";
    div.innerHTML = `<div class="row" style="justify-content:space-between;">
        <span>${escapeHtml(item.title)}</span>
        <span class="badge">${item.status}</span>
      </div>`;
    const prefill = document.createElement("button");
    prefill.textContent = "Prefill current eBay form";
    prefill.style.marginTop = "4px";
    prefill.onclick = () => prefillEbay(item);
    div.appendChild(prefill);
    container.appendChild(div);
  }
}

function fillMatchers() {
  el("matchItem").innerHTML = state.items
    .map((item) => `<option value="${item.id}">${escapeHtml(item.title)}</option>`)
    .join("");
  const ebayAccounts = state.accounts.filter((a) => (a.channel || "").toLowerCase() === "ebay");
  el("matchAccount").innerHTML = ebayAccounts.length
    ? ebayAccounts.map((a) => `<option value="${a.id}">${escapeHtml(a.displayName)}</option>`).join("")
    : `<option value="">No eBay channel connected</option>`;
}

async function readTab() {
  const tab = await activeTab();
  if (!tab?.url || !/ebay\.com/.test(tab.url)) {
    el("ebayRead").innerHTML = `<span class="err">Open an eBay tab first.</span>`;
    return;
  }
  try {
    const res = await chrome.tabs.sendMessage(tab.id, { type: "READ_LISTING" });
    if (!res?.ok) throw new Error(res?.error || "Could not read the page.");
    state.capturedListing = res.listing;
    el("ebayRead").innerHTML = `Captured: <strong>${escapeHtml(res.listing.title || "untitled")}</strong> · ${money(res.listing.price)}`;
    el("ebayMatch").classList.remove("hidden");
    autoSelectItem(res.listing.title);
  } catch (error) {
    el("ebayRead").innerHTML = `<span class="err">${escapeHtml(String(error.message || error))}</span>`;
  }
}

function autoSelectItem(title) {
  if (!title) return;
  const lower = title.toLowerCase();
  const match = state.items.find((item) => lower.includes((item.title || "").toLowerCase().slice(0, 12)));
  if (match) el("matchItem").value = match.id;
}

async function saveListed() {
  const itemId = el("matchItem").value;
  const channelAccountId = el("matchAccount").value;
  if (!itemId || !channelAccountId) {
    el("ebayMsg").innerHTML = `<span class="err">Pick an item and an eBay channel.</span>`;
    return;
  }
  try {
    await api.upsertListing(itemId, {
      channelAccountId,
      status: "Active",
      listedPrice: state.capturedListing?.price ?? null,
      quantity: null,
      externalListingId: state.capturedListing?.externalListingId ?? null,
      externalUrl: state.capturedListing?.url ?? null,
      imageHash: null,
    });
    el("ebayMsg").innerHTML = `<span class="ok">Saved to HaulScout.</span>`;
  } catch (error) {
    el("ebayMsg").innerHTML = `<span class="err">${escapeHtml(String(error.message || error))}</span>`;
  }
}

async function prefillEbay(item) {
  const tab = await activeTab();
  if (!tab?.url || !/ebay\.com/.test(tab.url)) {
    el("status").innerHTML = `<span class="err">Open an eBay sell page first.</span>`;
    return;
  }
  const res = await chrome.tabs.sendMessage(tab.id, {
    type: "PREFILL",
    item: { title: item.title, description: item.description, price: item.estimatedSalePrice },
  });
  el("status").textContent = res?.ok ? `Prefilled ${res.filled?.join(", ") || "nothing"} — review & submit.` : res?.error || "Prefill failed.";
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

async function init() {
  const config = await getConfig();
  el("apiBase").value = config.apiBase;
  el("token").value = config.token;
  el("save").onclick = async () => {
    await setConfig({ apiBase: el("apiBase").value, token: el("token").value });
    el("settingsMsg").textContent = "Saved.";
    await connect();
  };
  el("refresh").onclick = connect;
  el("readTab").onclick = readTab;
  el("saveListed").onclick = saveListed;
  if (config.token) connect();
  else el("settings").open = true;
}

init();
