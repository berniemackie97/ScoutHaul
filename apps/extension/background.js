// Message hub. Kept intentionally thin: the popup talks to the API directly and to content
// scripts via tabs.sendMessage. This worker exists for lifecycle hooks and future background sync.

chrome.runtime.onInstalled.addListener(() => {
  console.info("[HaulScout] Cross-Lister installed. Human-led mode: the extension prefills; you review and submit.");
});

// Relay: let content scripts ask the worker to broadcast (reserved for future delist-on-sale pushes).
chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === "PING") {
    sendResponse({ ok: true, at: Date.now() });
  }
  return false;
});
