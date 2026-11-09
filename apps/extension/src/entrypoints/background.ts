import { browser, type Browser } from "wxt/browser";
import { defineBackground } from "wxt/utils/define-background";
import { Agent } from "../agent/agent";
import { portalOrigins } from "../agent/config";
import { parseBridgeRequest, parsePopupRequest, type AgentResponse, type BridgeRequest, type BridgeStatus, type PopupRequest } from "../agent/messages";
import { afterUpdate } from "../agent/governor";
import { pairingItem, portalItem, queuedItem, runningItem, updateGovernors } from "../agent/storage";
import { SyncTabs } from "../agent/transport";

const heartbeatAlarm = "haulscout-heartbeat";

export default defineBackground(() => {
  const tabs = new SyncTabs();
  const agent = new Agent({ tabs });

  // Whatever a stopped background was in the middle of is over: start clean, and close any sync tab
  // it left open.
  void Promise.all([runningItem.setValue(null), queuedItem.setValue([]), tabs.sweep()]);

  // The heartbeat is an alarm, not a timer: it survives the background being stopped between checks.
  const ensureAlarm = () => browser.alarms.create(heartbeatAlarm, { periodInMinutes: 1, delayInMinutes: 0.1 });
  browser.runtime.onInstalled.addListener(({ reason }) => {
    void ensureAlarm();
    if (reason === "update") {
      void updateGovernors((states) => Object.fromEntries(Object.entries(states).map(([platform, state]) => [platform, afterUpdate(state)])));
    }
  });
  browser.runtime.onStartup.addListener(() => void ensureAlarm());
  browser.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === heartbeatAlarm) {
      void agent.checkIn();
    }
  });

  // Access to a marketplace was just granted: find out who this browser is signed in as there. Taken
  // away: tell HaulScout, so it stops handing this browser that marketplace's work.
  browser.permissions.onAdded.addListener(() => void agent.checkSessions({ force: true }));
  browser.permissions.onRemoved.addListener(() => void agent.checkIn());

  // A marketplace page finishing in one of the operator's tabs may mean they just signed in, or got
  // through a security check; the agent looks again. The browser gives the extension the address
  // only of pages it may read, so no other browsing is seen.
  // A change of address counts too: a sign-in form can open without a page load.
  browser.tabs.onUpdated.addListener((tabId, change, tab) => {
    if ((change.status === "complete" || change.url !== undefined) && tab.url) {
      void agent.noticePage(tabId, tab.url);
    }
  });

  browser.runtime.onMessage.addListener((message: unknown, sender: Browser.runtime.MessageSender) => {
    if (fromPopup(sender)) {
      const request = parsePopupRequest(message);
      return request ? handlePopup(agent, request) : Promise.resolve({ ok: false, error: "Invalid request." });
    }

    const portal = portalOf(sender);
    if (portal) {
      void portalItem.setValue(portal);
      const request = parseBridgeRequest(message);
      return request ? handleBridge(agent, request) : Promise.resolve({ ok: false, error: "Invalid request." });
    }

    return undefined;
  });

  void ensureAlarm();
});

function fromPopup(sender: Browser.runtime.MessageSender): boolean {
  return sender.id === browser.runtime.id && (sender.url?.startsWith(browser.runtime.getURL("/")) ?? false) && sender.tab === undefined;
}

/** The portal address a message came from, when it came from the bridge on one. */
function portalOf(sender: Browser.runtime.MessageSender): string | null {
  if (sender.id !== browser.runtime.id || sender.tab === undefined) {
    return null;
  }

  return portalOrigins.find((origin) => sender.url?.startsWith(`${origin}/`)) ?? null;
}

async function handlePopup(agent: Agent, request: PopupRequest): Promise<AgentResponse> {
  try {
    switch (request.type) {
      case "pair": {
        const pairing = await agent.pair(request.code, request.name);
        void agent.checkSessions({ force: true });
        return { ok: true, message: `Connected to ${pairing.organizationName}.` };
      }
      // Anything that reaches a marketplace is started, not awaited: it can take a minute, and the
      // popup follows it from storage — closing the popup must not look like stopping it.
      case "popup-opened":
        void agent.popupOpened();
        return { ok: true };
      case "refresh":
        void agent.checkSessions({ force: true });
        return { ok: true };
      case "scan":
        void agent.scanNow(request.channelAccountId, request.loop);
        return { ok: true };
      case "resume":
        void agent.resume(request.platform);
        return { ok: true };
      case "disconnect":
        await agent.disconnect();
        return { ok: true };
      default:
        return { ok: false, error: "Unknown request." };
    }
  } catch (error) {
    return { ok: false, error: error instanceof Error ? error.message : String(error) };
  }
}

async function handleBridge(agent: Agent, request: BridgeRequest): Promise<BridgeStatus | AgentResponse> {
  switch (request.type) {
    case "status": {
      const pairing = await pairingItem.getValue();
      return {
        installed: true,
        version: browser.runtime.getManifest().version,
        paired: pairing !== null,
        agentName: pairing?.agentName ?? null,
        organizationName: pairing?.organizationName ?? null,
      };
    }
    case "wake":
      // Don't hold the page waiting on a whole scan; it learns the outcome from the hub.
      void agent.checkIn();
      return { ok: true };
    default:
      return { ok: false, error: "Unknown request." };
  }
}
