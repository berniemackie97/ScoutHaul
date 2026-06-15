import { describe, it, expect, vi } from "vitest";

describe("API Router Structure", () => {
  it("exports appRouter and AppRouter type", async () => {
    const mod = await import("../src/index");
    expect(mod.appRouter).toBeDefined();
    expect(mod.createCallerFactory).toBeDefined();
  });

  it("appRouter has all expected procedure groups", async () => {
    const { appRouter } = await import("../src/root");
    const procedures = Object.keys(appRouter._def.procedures);

    const expectedGroups = [
      "products",
      "orders",
      "cart",
      "repairs",
      "consignments",
      "customers",
      "suppliers",
      "analytics",
      "blog",
      "newsletter",
    ];

    for (const group of expectedGroups) {
      const hasGroup = procedures.some((p) => p.startsWith(`${group}.`));
      expect(hasGroup, `Missing router group: ${group}`).toBe(true);
    }
  });
});
