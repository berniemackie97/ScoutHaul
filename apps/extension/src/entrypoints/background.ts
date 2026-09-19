import { browser, type Browser } from "wxt/browser";
import { defineBackground } from "wxt/utils/define-background";
import { Agent } from "../agent/agent";
import { portalOrigin } from "../agent/config";
import type { AgentResponse, BridgeRequest, BridgeStatus, PopupRequest } from "../agent/messages";
import { pairingItem } from "../agent/storage";

const heartbeatAlarm = "haulscout-heartbeat";

export default defineBackground(() => {
  const agent = new Agent();

  // The heartbeat is an alarm, not a timer: it survives the background being stopped between checks.
  const ensureAlarm = () => browser.alarms.create(heartbeatAlarm, { periodInMinutes: 1, delayInMinutes: 0.1 });
  browser.runtime.onInstalled.addListener(() => void ensureAlarm());
  browser.runtime.onStartup.addListener(() => void ensureAlarm());
  browser.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === heartbeatAlarm) {
      void agent.checkIn();
    }
  });

  // Access to a marketplace was just granted: find out who this browser is signed in as there.
  browser.permissions.onAdded.addListener(() => void agent.checkSessions());

  browser.runtime.onMessage.addListener((message: unknown, sender: Browser.runtime.MessageSender) => {
    if (fromPopup(sender)) {
      return handlePopup(agent, message as PopupRequest);
    }

    if (fromPortal(sender)) {
      return handleBridge(agent, message as BridgeRequest);
    }

    return undefined;
  });

  void ensureAlarm();
});

function fromPopup(sender: Browser.runtime.MessageSender): boolean {
  return sender.id === browser.runtime.id && (sender.url?.startsWith(browser.runtime.getURL("/")) ?? false) && sender.tab === undefined;
}

function fromPortal(sender: Browser.runtime.MessageSender): boolean {
  return sender.id === browser.runtime.id && sender.tab !== undefined && (sender.url?.startsWith(`${portalOrigin}/`) ?? false);
}

async function handlePopup(agent: Agent, request: PopupRequest): Promise<AgentResponse> {
  try {
    switch (request.type) {
      case "pair": {
        const pairing = await agent.pair(request.code, request.name);
        void agent.checkSessions();
        return { ok: true, message: `Connected to ${pairing.organizationName}.` };
      }
      case "check-in":
        await agent.checkIn();
        return { ok: true };
      case "check-sessions":
        await agent.checkSessions();
        return { ok: true };
      case "scan":
        await agent.scanNow(request.channelAccountId, request.loop);
        return { ok: true };
      case "resume":
        await agent.resume(request.platform);
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
