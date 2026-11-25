import { describe, expect, it, vi } from "vitest";
import { AdapterError } from "../adapters/types";
import { HeldBack, transientRetryDelaysMs, withTransientRetry } from "./runner";

const deps = () => ({ sleep: vi.fn(async () => undefined) });

describe("transient retry", () => {
  it("tries a network hiccup again after a pause, then succeeds", async () => {
    const sleeps = deps();
    let calls = 0;
    const result = await withTransientRetry(async () => {
      calls++;
      if (calls < 3) throw new AdapterError("Network", "The page took too long to load.");
      return "page";
    }, sleeps);

    expect(result).toBe("page");
    expect(calls).toBe(3);
    expect(sleeps.sleep).toHaveBeenCalledTimes(2);
    expect((sleeps.sleep.mock.calls[0] as unknown[])[0]).toBeGreaterThanOrEqual(transientRetryDelaysMs[0]);
  });

  it("gives up after the last pause and reports the failure", async () => {
    const sleeps = deps();
    await expect(withTransientRetry(async () => {
      throw new AdapterError("Network", "down");
    }, sleeps)).rejects.toThrow("down");
    expect(sleeps.sleep).toHaveBeenCalledTimes(transientRetryDelaysMs.length);
  });

  it.each([
    ["Challenge", new AdapterError("Challenge", "check")],
    ["RateLimited", new AdapterError("RateLimited", "slow")],
    ["SiteChanged", new AdapterError("SiteChanged", "changed")],
    ["governor", new HeldBack("paused")],
  ])("never retries a %s", async (_name, error) => {
    const sleeps = deps();
    let calls = 0;
    await expect(withTransientRetry(async () => {
      calls++;
      throw error;
    }, sleeps)).rejects.toBe(error);
    expect(calls).toBe(1);
    expect(sleeps.sleep).not.toHaveBeenCalled();
  });
});
