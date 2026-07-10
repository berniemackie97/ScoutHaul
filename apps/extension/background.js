// Message hub. Kept intentionally thin: the popup talks to the API directly and to content
// scripts via tabs.sendMessage. This worker exists for lifecycle hooks only.

chrome.runtime.onInstalled.addListener(() => {
  console.info("[HaulScout] Listing Helper installed. Human-led mode: prefill/capture only; you review and submit.");
});

// Relay: let content scripts ask the worker to broadcast visible operator events.
chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === "PING") {
    sendResponse({ ok: true, at: Date.now() });
  }
  return false;
});
