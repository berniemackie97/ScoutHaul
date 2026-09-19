import { beforeEach, describe, expect, it } from "vitest";
import { fakeBrowser } from "wxt/testing/fake-browser";
import { logActivity } from "./activity";
import { activityItem } from "./storage";

describe("the activity list", () => {
  beforeEach(() => fakeBrowser.reset());

  it("counts a line that repeats instead of repeating it, and moves it to the top", async () => {
    const at = (minute: number) => new Date(Date.UTC(2026, 8, 19, 12, minute));
    await logActivity("warning", "Signed out of eBay.", at(0));
    await logActivity("success", "Overhaul eBay sales: 3 updated orders.", at(1));
    await logActivity("warning", "Signed out of eBay.", at(2));
    await logActivity("warning", "Signed out of eBay.", at(3));

    expect(await activityItem.getValue()).toEqual([
      { at: at(3).toISOString(), tone: "warning", message: "Signed out of eBay.", count: 3 },
      { at: at(1).toISOString(), tone: "success", message: "Overhaul eBay sales: 3 updated orders." },
    ]);
  });

  it("starts a new line once the old one is half an hour behind", async () => {
    await logActivity("warning", "Signed out of eBay.", new Date("2026-09-19T12:00:00Z"));
    await logActivity("warning", "Signed out of eBay.", new Date("2026-09-19T12:31:00Z"));

    expect(await activityItem.getValue()).toHaveLength(2);
  });
});
