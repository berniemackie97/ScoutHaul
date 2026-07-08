import { api, getConfig, setConfig } from "./api.js";

const el = (id) => document.getElementById(id);
const state = { items: [], accounts: [], scraped: null, scrapedOrders: [] };

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
    el("ordersCard").classList.remove("hidden");
    el("status").textContent = `${state.items.length} items · ${state.accounts.length} channels`;
    loadDelist();
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
    prefill.textContent = "Prefill active eBay tab";
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
    state.scraped = res.listing;
    el("ebayRead").innerHTML = `Read: <strong>${escapeHtml(res.listing.title || "untitled")}</strong> · ${money(res.listing.price)}`;
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
      listedPrice: state.scraped?.price ?? null,
      quantity: null,
      externalListingId: state.scraped?.externalListingId ?? null,
      externalUrl: state.scraped?.url ?? null,
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

async function readOrders() {
  const tab = await activeTab();
  if (!tab?.url || !/ebay\.com/.test(tab.url)) {
    el("ordersRead").innerHTML = `<span class="err">Open your eBay Seller Hub orders tab first.</span>`;
    return;
  }
  el("pushOrders").classList.add("hidden");
  el("ordersMsg").textContent = "";
  try {
    const res = await chrome.tabs.sendMessage(tab.id, { type: "READ_ORDERS" });
    if (!res?.ok) throw new Error(res?.error || "Could not read the page.");
    state.scrapedOrders = res.orders || [];
    const diag = res.diagnostics || {};
    const warn = diag.warning ? `<div class="err">${escapeHtml(diag.warning)}</div>` : "";
    el("ordersRead").innerHTML =
      `Found <strong>${state.scrapedOrders.length}</strong> order(s) · ${diag.rowsFound ?? 0} rows, ${diag.skipped ?? 0} skipped.${warn}`;
    if (state.scrapedOrders.length) el("pushOrders").classList.remove("hidden");
  } catch (error) {
    el("ordersRead").innerHTML = `<span class="err">${escapeHtml(String(error.message || error))}</span>`;
  }
}

async function pushOrders() {
  const orders = state.scrapedOrders || [];
  if (!orders.length) return;
  el("pushOrders").disabled = true;
  el("ordersMsg").textContent = "Pushing…";
  try {
    const result = await api.ingestPlatformOrders("eBay", orders);
    el("ordersMsg").innerHTML = `<span class="ok">Imported ${result.created}, updated ${result.updated}, skipped ${result.skipped}. Reconcile in Finance.</span>`;
    state.scrapedOrders = [];
    el("pushOrders").classList.add("hidden");
  } catch (error) {
    el("ordersMsg").innerHTML = `<span class="err">${escapeHtml(String(error.message || error))}</span>`;
  } finally {
    el("pushOrders").disabled = false;
  }
}

async function loadDelist() {
  const card = el("delistCard");
  const container = el("delistList");
  try {
    const listings = await api.worklist("PendingDelist");
    const rows = Array.isArray(listings) ? listings : [];
    if (rows.length === 0) {
      card.classList.add("hidden");
      return;
    }
    card.classList.remove("hidden");
    container.innerHTML = "";
    for (const listing of rows) {
      const div = document.createElement("div");
      div.className = "item";
      const link = listing.externalUrl
        ? `<a href="${escapeHtml(listing.externalUrl)}" target="_blank" rel="noreferrer">open</a>`
        : "";
      div.innerHTML = `<div class="row" style="justify-content:space-between;">
          <span>${escapeHtml(listing.inventoryItemTitle)} <span class="muted">· ${escapeHtml(listing.channelAccountName)}</span></span>
          <span>${link}</span>
        </div>`;
      const done = document.createElement("button");
      done.textContent = "Mark delisted";
      done.style.marginTop = "4px";
      done.onclick = async () => {
        done.disabled = true;
        try {
          await api.upsertListing(listing.inventoryItemId, {
            channelAccountId: listing.channelAccountId,
            status: "Delisted",
            listedPrice: listing.listedPrice ?? null,
            quantity: listing.quantity ?? null,
            externalListingId: listing.externalListingId ?? null,
            externalUrl: listing.externalUrl ?? null,
            imageHash: null,
          });
          div.remove();
          if (!container.children.length) card.classList.add("hidden");
        } catch (error) {
          done.disabled = false;
          done.textContent = String(error.message || error).slice(0, 40);
        }
      };
      div.appendChild(done);
      container.appendChild(div);
    }
  } catch {
    card.classList.add("hidden");
  }
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
  el("readOrders").onclick = readOrders;
  el("pushOrders").onclick = pushOrders;
  if (config.token) connect();
  else el("settings").open = true;
}

init();
