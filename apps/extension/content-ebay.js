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
