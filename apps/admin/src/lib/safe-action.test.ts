import { describe, expect, it } from "vitest";
import { safely } from "./safe-action";

describe("safely", () => {
  it("passes an action's own outcome through", async () => {
    expect(await safely(async () => ({ ok: true, message: "Done." }))).toEqual({ ok: true, message: "Done." });
  });

  it("turns a thrown action into a failed outcome", async () => {
    expect(await safely(async () => { throw new TypeError("fetch failed"); }, "Offline.")).toEqual({ ok: false, message: "Offline." });
  });
});
