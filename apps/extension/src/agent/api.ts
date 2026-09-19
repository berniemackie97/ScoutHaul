import { createApiClient } from "@overhaul/api-client";
import { apiOrigin } from "./config";
import type {
  AgentRun,
  Heartbeat,
  HeartbeatRequest,
  IngestListing,
  IngestOrder,
  LoopKind,
  FailureKind,
  Pairing,
  RunIssue,
  SyncRun,
  Trigger,
} from "./types";

/**
 * A refusal or failure from the API, with the machine code the server sent — the agent branches on
 * codes (`sync_lease_held`, `agent_revoked`, …), never on the English message.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }

  /** The device token no longer works: the browser must be connected again. */
  get disconnected(): boolean {
    return this.status === 401 && (this.code?.startsWith("agent_") ?? false);
  }
}

type Problem = { detail?: string | null; title?: string | null; code?: string | null };

async function unwrap<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  let result: { data?: T; error?: unknown; response: Response };
  try {
    result = await call;
  } catch (cause) {
    throw new ApiError(0, "network", `HaulScout could not be reached (${cause instanceof Error ? cause.message : "network error"}).`);
  }

  if (!result.response.ok) {
    const problem = (result.error ?? {}) as Problem;
    throw new ApiError(
      result.response.status,
      problem.code ?? undefined,
      problem.detail ?? problem.title ?? `HaulScout answered ${result.response.status}.`,
    );
  }

  return result.data as T;
}

/** The API as the agent uses it, authenticated with the device token this browser holds. */
export function createAgentApi(getToken: () => Promise<string | null>) {
  const client = createApiClient({ baseUrl: apiOrigin, getAccessToken: getToken });

  return {
    pair: (code: string, browser: string, extensionVersion: string, operatingSystem: string | null, name: string | null) =>
      unwrap<Pairing>(client.POST("/api/sync/agent/pair", { body: { code, browser, extensionVersion, operatingSystem, name } })),

    heartbeat: (body: HeartbeatRequest) => unwrap<Heartbeat>(client.POST("/api/sync/agent/heartbeat", { body })),

    startRun: (body: {
      channelAccountId: string;
      loop: LoopKind;
      trigger: Trigger;
      signedInAs: string;
      scanRequestId?: string | null;
      marketplaceUserId?: string | null;
    }) => unwrap<AgentRun>(client.POST("/api/sync/agent/runs", { body })),

    addBatch: (runId: string, body: { orders?: IngestOrder[]; listings?: IngestListing[]; issues?: RunIssue[] }) =>
      unwrap(client.POST("/api/sync/agent/runs/{runId}/batches", { params: { path: { runId } }, body })),

    complete: (runId: string, body: { cursor?: string | null; coversFrom?: string | null; coversTo?: string | null; completeSnapshot?: boolean }) =>
      unwrap<SyncRun>(client.POST("/api/sync/agent/runs/{runId}/complete", { params: { path: { runId } }, body })),

    fail: (runId: string, kind: FailureKind, message: string | null) =>
      unwrap<SyncRun>(client.POST("/api/sync/agent/runs/{runId}/fail", { params: { path: { runId } }, body: { kind, message } })),

    /** Work that could not start — nothing read, no run opened — so a scan someone asked for says why. */
    reportWorkFailure: (body: { channelAccountId: string; loop: LoopKind; kind: FailureKind; message?: string | null; scanRequestId?: string | null }) =>
      unwrap(client.POST("/api/sync/agent/work-failures", { body })),

    disconnect: () => unwrap(client.POST("/api/sync/agent/disconnect", {})),
  };
}

export type AgentApi = ReturnType<typeof createAgentApi>;
