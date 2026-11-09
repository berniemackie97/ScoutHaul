import { describe, expect, it } from "vitest";
import { parseBridgeRequest, parsePopupRequest } from "./messages";

describe("extension message validation", () => {
  it("accepts the actions the popup sends with bounded arguments", () => {
    expect(parsePopupRequest({ type: "pair", code: "ABC123", name: null })).toEqual({ type: "pair", code: "ABC123", name: null });
    expect(parsePopupRequest({ type: "scan", channelAccountId: "123", loop: "Sales" })).toEqual({ type: "scan", channelAccountId: "123", loop: "Sales" });
    expect(parsePopupRequest({ type: "resume", platform: "eBay" })).toEqual({ type: "resume", platform: "eBay" });
    expect(parsePopupRequest({ type: "disconnect" })).toEqual({ type: "disconnect" });
    expect(parsePopupRequest({ type: "refresh" })).toEqual({ type: "refresh" });
  });

  it("rejects malformed commands and strips unexpected fields", () => {
    for (const request of [
      null, [], "disconnect", { type: "pair", code: {}, name: null },
      { type: "pair", code: "x".repeat(129), name: null },
      { type: "pair", code: "x", name: 19 },
      { type: "scan", channelAccountId: "123", loop: "Delete" },
      { type: "scan", channelAccountId: "", loop: "Sales" },
      { type: "resume", platform: "" },
      { type: "work", action: "sync" },
    ]) expect(parsePopupRequest(request)).toBeNull();
    expect(parsePopupRequest({ type: "disconnect", token: "untrusted" })).toEqual({ type: "disconnect" });
    expect(parsePopupRequest({ type: "scan", channelAccountId: "123", loop: "Listings", auth: "ignored" }))
      .toEqual({ type: "scan", channelAccountId: "123", loop: "Listings" });
  });

  it("accepts only status and wake from portal content scripts", () => {
    expect(parseBridgeRequest({ type: "status", token: "hidden" })).toEqual({ type: "status" });
    expect(parseBridgeRequest({ type: "wake" })).toEqual({ type: "wake" });
    for (const request of [null, "wake", [], { type: "pair", code: "ABC" }, { type: "scan" }, { type: 42 }]) {
      expect(parseBridgeRequest(request)).toBeNull();
    }
  });
});
