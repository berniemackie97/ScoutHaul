import { describe, expect, it } from "vitest";
import { confirmLabel, initialChoice, mappingRows, summarize, type LineChoice } from "./reconcile-model";

describe("matching order lines", () => {
  it("pre-selects an exact match, never a title guess", () => {
    expect(initialChoice({ id: "l1", suggestedInventoryItemId: "i1", suggestedInventoryItemTitle: "Lamp", suggestionExact: true }))
      .toEqual({ kind: "item", id: "i1", title: "Lamp", isSold: false });
    expect(initialChoice({ id: "l1", suggestedInventoryItemId: "i1", suggestedInventoryItemTitle: "Wii Sports Disc Only", suggestionExact: false }))
      .toBeNull();
    expect(initialChoice({ id: "l1", inventoryItemId: "i9", inventoryItemTitle: "Mapped" })?.kind).toBe("item");
  });

  it("says what confirming would do, including items created from the sale", () => {
    const lines = [{ id: "a" }, { id: "b" }, { id: "c" }];
    const choices: Record<string, LineChoice | null> = {
      a: { kind: "item", id: "i1", title: "Lamp", isSold: false },
      b: { kind: "item", id: "i2", title: "Shade", isSold: true },
      c: { kind: "create" },
    };

    const summary = summarize(lines, choices);
    expect(summary).toEqual({ complete: true, distinct: true, record: 1, correct: 1, create: 1 });
    expect(confirmLabel(summary)).toBe("record 1 · correct 1 · create 1 item");
    expect(mappingRows(lines, choices)).toEqual([
      { platformOrderLineId: "a", inventoryItemId: "i1" },
      { platformOrderLineId: "b", inventoryItemId: "i2" },
      { platformOrderLineId: "c", createItem: true },
    ]);
  });

  it("blocks confirming until every line is decided, and each item is used once", () => {
    const lines = [{ id: "a" }, { id: "b" }];
    expect(summarize(lines, { a: { kind: "create" }, b: null }).complete).toBe(false);
    const same = { kind: "item" as const, id: "i1", title: "Lamp", isSold: false };
    expect(summarize(lines, { a: same, b: same }).distinct).toBe(false);
    expect(confirmLabel(summarize(lines, {}))).toBe("Match every line first");
  });
});
