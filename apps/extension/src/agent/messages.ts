import type { LoopKind } from "./types";

/**
 * What the popup may ask the background agent to do. Anything that reaches a marketplace is started
 * and answered at once; the popup follows its progress from storage, so closing it stops nothing.
 */
export type PopupRequest =
  | { type: "pair"; code: string; name: string | null }
  | { type: "popup-opened" }
  | { type: "refresh" }
  | { type: "scan"; channelAccountId: string; loop: LoopKind }
  | { type: "resume"; platform: string }
  | { type: "disconnect" };

/**
 * What a HaulScout portal page may ask, through the bridge. Deliberately small: a page can learn
 * whether this browser is connected and ask it to check in now — nothing that changes what it syncs
 * or for whom. Pairing happens in the popup, where only the person at the browser can do it.
 */
export type BridgeRequest = { type: "status" } | { type: "wake" };

export type AgentResponse = { ok: true; message?: string } | { ok: false; error: string };

export interface BridgeStatus {
  installed: true;
  version: string;
  paired: boolean;
  agentName: string | null;
  organizationName: string | null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function boundedText(value: unknown, maxLength: number): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= maxLength;
}

export function parsePopupRequest(value: unknown): PopupRequest | null {
  if (!isRecord(value)) return null;
  switch (value.type) {
    case "pair":
      return boundedText(value.code, 128) && (value.name === null || boundedText(value.name, 80))
        ? { type: "pair", code: value.code, name: value.name } : null;
    case "popup-opened":
      return { type: "popup-opened" };
    case "refresh":
      return { type: "refresh" };
    case "disconnect":
      return { type: "disconnect" };
    case "scan":
      return boundedText(value.channelAccountId, 64) && (value.loop === "Sales" || value.loop === "Listings")
        ? { type: "scan", channelAccountId: value.channelAccountId, loop: value.loop } : null;
    case "resume":
      return boundedText(value.platform, 50) ? { type: "resume", platform: value.platform } : null;
    default:
      return null;
  }
}

export function parseBridgeRequest(value: unknown): BridgeRequest | null {
  if (!isRecord(value)) return null;
  if (value.type === "status") return { type: "status" };
  if (value.type === "wake") return { type: "wake" };
  return null;
}
