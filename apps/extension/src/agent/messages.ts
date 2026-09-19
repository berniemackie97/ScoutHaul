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
