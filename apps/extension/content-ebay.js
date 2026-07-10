// eBay content script.
//
// SCOPE — deliberately small, and it stays small:
//   ACT     prefill the sell form from a ScoutHaul inventory item, so a human reviews and submits.
//   CAPTURE read the listing page the operator is already standing on, to link it back to the item.
//
// It NEVER auto-submits, never runs headless, and never bulk-reads list pages.
//
// Why no order/list scraping: bulk DOM scraping of Seller Hub is (a) against eBay's User Agreement, and
// (b) the part that rots every time eBay reskins a page. Order, fee, refund, and payout data comes from
// eBay's own CSV report exports, which are documented and have a stable schema. Two DOM surfaces per
// platform — a form and a link — is the entire brittle footprint we are willing to carry.

/** Reads the listing page currently open. Returns nulls for anything it cannot read — never a guess. */
function readListing() {
  const title =
    document.querySelector('input[name="title"]')?.value ||
    document.querySelector("h1")?.textContent?.trim() ||
    null;
  const priceEl = document.querySelector('input[name="price"], .x-price-primary span');
  const price = priceEl ? Number(String(priceEl.value ?? priceEl.textContent).replace(/[^0-9.]/g, "")) || null : null;
  const url = location.href;
  const externalListingId = url.match(/\/itm\/(\d+)/)?.[1] || null;
  return { title, price, url, externalListingId };
}

/** Prefills the sell form. Missing fields are skipped and reported, never silently guessed. */
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
  const skipped = [];
  const tryFill = (name, selector, value) => (setValue(selector, value) ? filled.push(name) : skipped.push(name));

  tryFill("title", 'input[name="title"]', item.title);
  tryFill("description", 'textarea[name="description"]', item.description);
  tryFill("price", 'input[name="price"]', item.price);

  return { filled, skipped };
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  try {
    if (message?.type === "READ_LISTING") {
      sendResponse({ ok: true, listing: readListing() });
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
