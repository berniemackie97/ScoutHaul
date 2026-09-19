import { beforeEach, describe, expect, it, vi } from "vitest";
import { fakeBrowser } from "wxt/testing/fake-browser";
import { Agent } from "./agent";
import { ApiError, type AgentApi } from "./api";
import { authProblemItem, heartbeatItem, pairingItem } from "./storage";

const pairing = { agentId: "agent", agentName: "Laptop", organizationName: "Overhaul", token: "hsa_old", pairedAt: "2026-09-18T00:00:00Z" };

function agentWith(heartbeat: AgentApi["heartbeat"]) {
  const api = { heartbeat: vi.fn(heartbeat) } as unknown as AgentApi;
  return { agent: new Agent({ api, openTab: vi.fn(), now: () => new Date("2026-09-18T20:00:00Z"), sleep: async () => undefined }), api };
}

describe("checking in", () => {
  beforeEach(() => {
    fakeBrowser.reset();
    fakeBrowser.permissions.contains = vi.fn(async () => false) as typeof fakeBrowser.permissions.contains;
    fakeBrowser.runtime.getManifest = vi.fn(() => ({ manifest_version: 3 as const, name: "HaulScout Sync", version: "0.2.0" }));
  });

  it("does nothing until the browser is connected", async () => {
    const { agent, api } = agentWith(async () => ({}));
    await agent.checkIn();
    expect(api.heartbeat).not.toHaveBeenCalled();
  });

  it("reports what it can see, and keeps a rotated token", async () => {
    await pairingItem.setValue(pairing);
    const { agent, api } = agentWith(async () => ({ agentName: "Office mini-PC", organizationName: "Overhaul", newToken: "hsa_new", work: [] }));

    await agent.checkIn();

    expect(api.heartbeat).toHaveBeenCalledWith(expect.objectContaining({
      marketplaces: [expect.objectContaining({ platform: "eBay", accessGranted: false })],
    }));
    expect(await pairingItem.getValue()).toMatchObject({ token: "hsa_new", agentName: "Office mini-PC" });
    expect((await heartbeatItem.getValue())?.newToken).toBeNull();
  });

  it("coalesces check-ins that arrive together into one", async () => {
    await pairingItem.setValue(pairing);
    const { agent, api } = agentWith(async () => ({ work: [] }));

    await Promise.all([agent.checkIn(), agent.checkIn(), agent.checkIn()]);

    expect(api.heartbeat).toHaveBeenCalledTimes(1);
  });

  it("forgets a token HaulScout no longer accepts, and says why", async () => {
    await pairingItem.setValue(pairing);
    const { agent } = agentWith(async () => {
      throw new ApiError(401, "agent_revoked", "This browser was disconnected from HaulScout.");
    });

    await agent.checkIn();

    expect(await pairingItem.getValue()).toBeNull();
    expect(await authProblemItem.getValue()).toMatchObject({ code: "agent_revoked", message: "This browser was disconnected from HaulScout." });
  });

  it("keeps the connection through a network failure", async () => {
    await pairingItem.setValue(pairing);
    const { agent } = agentWith(async () => {
      throw new ApiError(0, "network", "HaulScout could not be reached.");
    });

    await agent.checkIn();

    expect(await pairingItem.getValue()).toEqual(pairing);
  });
});
