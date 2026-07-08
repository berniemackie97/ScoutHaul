// eBay content script. Compliance is the whole point: this NEVER auto-submits and only acts on an
// explicit operator action from the popup. It (a) reads the current listing so state syncs back to
// HaulScout, and (b) prefills the sell form so a human can review and submit.

function readListing() {
  const title =
    document.querySelector('input[name="title"], input#s0-1-...-title, [data-testid="title"] input')?.value ||
    document.querySelector("h1")?.textContent?.trim() ||
    null;
  const priceEl = document.querySelector('input[name="price"], [data-testid="price"] input, .x-price-primary span');
  const price = priceEl ? Number(String(priceEl.value ?? priceEl.textContent).replace(/[^0-9.]/g, "")) || null : null;
  const url = location.href;
  const externalListingId = url.match(/\/itm\/(\d+)/)?.[1] || null;
  return { title, price, url, externalListingId };
}

// Best-effort prefill. Selectors drift; missing fields are skipped, never guessed destructively.
function prefill(item) {
  const setValue = (selector, value) => {
    const el = document.querySelector(selector);
    if (!el || value == null) return false;
    el.focus();
    el.value = String(value);
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
    return true;
  };
  const filled = [];
  if (setValue('input[name="title"]', item.title)) filled.push("title");
  if (setValue('textarea[name="description"], [data-testid="description"] textarea', item.description)) filled.push("description");
  if (setValue('input[name="price"]', item.price)) filled.push("price");
  return { filled };
}

// ----------------------------------------------------------------------------------------------------
// Order READ adapter. Scrapes the operator's own Seller Hub order rows into the normalized shape the API's
// /api/platform-orders/ingest endpoint expects. It NEVER guesses money: a field it cannot read stays 0/null
// and the row is flagged in diagnostics, so the operator always sees exactly what was and wasn't captured.
// eBay selectors drift — everything the parser depends on lives in ORDER_SELECTORS for easy, isolated tuning.
// ----------------------------------------------------------------------------------------------------

const ORDER_SELECTORS = {
  // A single order on the Seller Hub Orders page. Multiple fallbacks; the first that matches rows wins.
  row: [
    '[data-testid="order-card"]',
    ".order-info-container",
    "tr.order-row",
    '[class*="order-row"]',
  ],
  orderIdLink: ['a[href*="orderid="]', 'a[href*="orderId="]', 'a[href*="/ord/details"]'],
  buyer: ['[data-testid="buyer-username"]', '[class*="buyer"] a', '[class*="buyer-name"]'],
  orderDate: ['[data-testid="order-date"]', "time", '[class*="order-date"]'],
  itemTitle: ['[data-testid="item-title"]', ".item-title", 'a[href*="/itm/"]'],
  itemLink: ['a[href*="/itm/"]'],
  total: ['[data-testid="order-total"]', '[class*="order-total"]', '[class*="total"]'],
};

function parseMoney(text) {
  if (text == null) return null;
  const match = String(text).replace(/,/g, "").match(/-?\d+(\.\d+)?/);
  return match ? Number(match[0]) : null;
}

function firstMatch(root, selectors) {
  for (const selector of selectors) {
    const el = root.querySelector(selector);
    if (el) return el;
  }
  return null;
}

function orderIdFromHref(href) {
  if (!href) return null;
  return (
    new URL(href, location.origin).searchParams.get("orderid") ||
    new URL(href, location.origin).searchParams.get("orderId") ||
    href.match(/order(?:id|Id)[=/](\w[\w-]+)/)?.[1] ||
    null
  );
}

function readOrderRow(row) {
  const idLink = firstMatch(row, ORDER_SELECTORS.orderIdLink);
  const externalOrderId = orderIdFromHref(idLink?.getAttribute("href"));
  if (!externalOrderId) return { skipped: "no order id" };

  const itemEl = firstMatch(row, ORDER_SELECTORS.itemTitle);
  const itemLinkEl = firstMatch(row, ORDER_SELECTORS.itemLink);
  const title = itemEl?.textContent?.trim() || "Untitled item";
  const externalItemId = itemLinkEl?.getAttribute("href")?.match(/\/itm\/(\d+)/)?.[1] || null;

  const total = parseMoney(firstMatch(row, ORDER_SELECTORS.total)?.textContent);
  const dateText = firstMatch(row, ORDER_SELECTORS.orderDate)?.getAttribute("datetime")
    || firstMatch(row, ORDER_SELECTORS.orderDate)?.textContent?.trim();
  const orderedAt = dateText && !Number.isNaN(Date.parse(dateText)) ? new Date(dateText).toISOString() : new Date().toISOString();
  const buyer = firstMatch(row, ORDER_SELECTORS.buyer)?.textContent?.trim() || null;

  // The Orders page shows the buyer-facing total but not the fee stack — that is on the Payments/Payouts page.
  // We capture the item price here; fees/tax/payout stay 0/null until a payments read fills them in.
  const unitPrice = total ?? 0;
  return {
    order: {
      externalOrderId,
      orderedAt,
      itemsSubtotal: unitPrice,
      shippingCharged: 0,
      taxCollected: 0,
      feeTotal: 0,
      payoutAmount: null,
      buyerReference: buyer,
      lines: [{ title, unitPrice, quantity: 1, externalItemId }],
      rawJson: JSON.stringify({ text: row.textContent?.replace(/\s+/g, " ").trim().slice(0, 500) }),
    },
  };
}

function readOrders() {
  let rows = [];
  for (const selector of ORDER_SELECTORS.row) {
    rows = Array.from(document.querySelectorAll(selector));
    if (rows.length) break;
  }

  const orders = [];
  let skipped = 0;
  for (const row of rows) {
    const result = readOrderRow(row);
    if (result.order) orders.push(result.order);
    else skipped++;
  }

  const diagnostics = {
    url: location.href,
    rowsFound: rows.length,
    parsed: orders.length,
    skipped,
    // Loud, explicit signal so the operator never trusts an empty/partial read as "no orders".
    warning:
      rows.length === 0
        ? "No order rows matched. Open Seller Hub → Orders, or update ORDER_SELECTORS for the current eBay layout."
        : orders.some((order) => order.feeTotal === 0)
          ? "Item prices captured, but fees/payout are not on this page — also read Payments → Payouts for exact fees."
          : null,
  };
  return { orders, diagnostics };
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  try {
    if (message?.type === "READ_LISTING") {
      sendResponse({ ok: true, listing: readListing() });
    } else if (message?.type === "READ_ORDERS") {
      sendResponse({ ok: true, ...readOrders() });
    } else if (message?.type === "PREFILL") {
      const result = prefill(message.item || {});
      // Deliberately no form submission — the operator reviews and submits by hand.
      sendResponse({ ok: true, ...result, note: "Prefilled. Review and submit manually." });
    } else {
      sendResponse({ ok: false, error: "Unknown message" });
    }
  } catch (error) {
    sendResponse({ ok: false, error: String(error?.message || error) });
  }
  return true;
});
