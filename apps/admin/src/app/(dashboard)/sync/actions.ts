"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

type ScanScope = components["schemas"]["SyncScanScope"];
type LoopKind = components["schemas"]["SyncLoopKind"];

export type SyncActionResult = { ok: boolean; message: string };

function refresh() {
  revalidatePath("/sync");
  revalidatePath("/sync/browsers");
}

/** Asks for a scan now. Says what was queued, and names anything it could not queue and why. */
export async function requestScan(scope: ScanScope, channelAccountId: string | null, from: string | null = null): Promise<SyncActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/sync/scans", { body: { scope, channelAccountId, from } });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The scan could not be requested.") };

  refresh();
  const queued = result.data.requests?.length ?? 0;
  const skipped = result.data.skipped ?? [];
  const lead = queued === 0 ? "Nothing was queued." : queued === 1 ? "Scan queued — follow it below." : `${queued} scans queued — follow them below.`;
  const why = skipped.map((item) => `${item.accountName}: ${item.reason}`).join(" ");
  return { ok: queued > 0, message: why ? `${lead} ${why}` : lead };
}

export async function cancelScan(requestId: string): Promise<SyncActionResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/sync/scans/{requestId}/cancel", { params: { path: { requestId } } });
  // A 204 carries no body; the status says whether it worked.
  if (!result.response.ok) return { ok: false, message: errorMessage((result as { error?: unknown }).error, "The scan could not be cancelled.") };

  refresh();
  return { ok: true, message: "Scan cancelled." };
}

/** Sets one loop's schedule, keeping the other as it is. Null switches it off. */
export async function setSchedule(
  channelAccountId: string,
  loop: LoopKind,
  minutes: number | null,
  current: { sales: number | null; listings: number | null },
): Promise<SyncActionResult> {
  const api = await getApiClient();
  const result = await api.PUT("/api/sync/accounts/{accountId}/schedule", {
    params: { path: { accountId: channelAccountId } },
    body: {
      salesEveryMinutes: loop === "Sales" ? minutes : current.sales,
      listingsEveryMinutes: loop === "Listings" ? minutes : current.listings,
    },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The schedule could not be saved.") };

  refresh();
  return { ok: true, message: minutes ? "Schedule saved." : `Scheduled ${loop.toLowerCase()} scans switched off.` };
}

/**
 * Switches keeping an item's photos in step with this account's listings on or off. Switching it on hands the
 * listings' photos to the items they are linked to, replacing the photos those items show.
 */
export async function setPhotoSync(channelAccountId: string, enabled: boolean): Promise<SyncActionResult> {
  const api = await getApiClient();
  const result = await api.PUT("/api/sync/accounts/{accountId}/photos", {
    params: { path: { accountId: channelAccountId } },
    body: { enabled },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The photo setting could not be saved.") };

  refresh();
  const waiting = result.data.accounts?.find((account) => account.channelAccountId === channelAccountId)?.photosWaiting ?? 0;
  if (!enabled) return { ok: true, message: "Photos no longer follow this account's listings." };
  return {
    ok: true,
    message: waiting > 0
      ? `Photos now follow this account's listings. ${waiting} ${waiting === 1 ? "listing is" : "listings are"} on the way.`
      : "Photos now follow this account's listings.",
  };
}

export async function bindAccount(channelAccountId: string, username: string): Promise<SyncActionResult> {
  const api = await getApiClient();
  const result = await api.PUT("/api/sync/accounts/{accountId}/binding", {
    params: { path: { accountId: channelAccountId } },
    body: { username },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The account could not be bound.") };

  refresh();
  return { ok: true, message: `Bound to ${username}.` };
}

export async function unbindAccount(channelAccountId: string): Promise<SyncActionResult> {
  const api = await getApiClient();
  const result = await api.DELETE("/api/sync/accounts/{accountId}/binding", { params: { path: { accountId: channelAccountId } } });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The account could not be unbound.") };

  refresh();
  return { ok: true, message: "Unbound. It syncs nothing until it is bound again." };
}

export async function createPairing(): Promise<{ ok: true; pairingId: string; code: string; expiresAt: string } | { ok: false; message: string }> {
  const api = await getApiClient();
  const result = await api.POST("/api/sync/pairings", {});
  if (!result.data?.pairingId || !result.data.code || !result.data.expiresAt) {
    return { ok: false, message: errorMessage(result.error, "A code could not be created.") };
  }

  return { ok: true, pairingId: result.data.pairingId, code: result.data.code, expiresAt: result.data.expiresAt };
}

export async function renameBrowser(agentId: string, name: string): Promise<SyncActionResult> {
  const api = await getApiClient();
  const result = await api.PATCH("/api/sync/agents/{agentId}", { params: { path: { agentId } }, body: { name } });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The browser could not be renamed.") };

  refresh();
  return { ok: true, message: "Renamed." };
}

export async function disconnectBrowser(agentId: string): Promise<SyncActionResult> {
  const api = await getApiClient();
  const result = await api.DELETE("/api/sync/agents/{agentId}", { params: { path: { agentId } } });
  if (!result.response.ok) return { ok: false, message: errorMessage((result as { error?: unknown }).error, "The browser could not be disconnected.") };

  refresh();
  return { ok: true, message: "Disconnected. Its token stopped working immediately." };
}
