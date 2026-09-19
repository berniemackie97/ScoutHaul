import type { components } from "@overhaul/api-client";

type PlatformOrderLine = components["schemas"]["PlatformOrderLineDto"];

/** What a line has been matched to: an existing item, or one to create from the sale itself. */
export type LineChoice =
  | { kind: "item"; id: string; title: string; isSold: boolean }
  | { kind: "create" };

/**
 * What a line starts as. Already mapped: that item. An exact suggestion — the platform's item number
 * or the SKU matched — is pre-selected. A title resemblance never is: it is offered, with a warning,
 * and a person picks it or not. Pre-selecting a guess is how a lot of four games got matched to a
 * single disc and nearly rewrote that disc's sale.
 */
export function initialChoice(line: PlatformOrderLine): LineChoice | null {
  if (line.inventoryItemId) {
    return { kind: "item", id: line.inventoryItemId, title: line.inventoryItemTitle ?? "Matched item", isSold: false };
  }

  if (line.suggestedInventoryItemId && line.suggestionExact) {
    return {
      kind: "item",
      id: line.suggestedInventoryItemId,
      title: line.suggestedInventoryItemTitle ?? "Suggested item",
      isSold: line.suggestedInventoryItemAlreadySold ?? false,
    };
  }

  return null;
}

/** What confirming would do, and whether it can. */
export function summarize(lines: PlatformOrderLine[], choices: Record<string, LineChoice | null>) {
  const chosen = lines.map((line) => choices[line.id ?? ""] ?? null);
  const itemIds = chosen.flatMap((choice) => (choice?.kind === "item" ? [choice.id] : []));
  return {
    complete: lines.length > 0 && chosen.every((choice) => choice !== null),
    distinct: new Set(itemIds).size === itemIds.length,
    record: chosen.filter((choice) => choice?.kind === "item" && !choice.isSold).length,
    correct: chosen.filter((choice) => choice?.kind === "item" && choice.isSold).length,
    create: chosen.filter((choice) => choice?.kind === "create").length,
  };
}

/** One line of the mapping the server takes: an item, or "create one from the sale". */
export type MappingRow = { platformOrderLineId: string; inventoryItemId?: string; createItem?: boolean };

/** The mapping the server takes. */
export function mappingRows(lines: PlatformOrderLine[], choices: Record<string, LineChoice | null>): MappingRow[] {
  return lines.flatMap<MappingRow>((line) => {
    const choice = line.id ? choices[line.id] : null;
    if (!line.id || !choice) return [];
    return choice.kind === "create"
      ? [{ platformOrderLineId: line.id, createItem: true }]
      : [{ platformOrderLineId: line.id, inventoryItemId: choice.id }];
  });
}

/** "record 1 · correct 1 · create 2 items" — only what will happen. */
export function confirmLabel(summary: ReturnType<typeof summarize>): string {
  const parts = [
    summary.record > 0 ? `record ${summary.record}` : null,
    summary.correct > 0 ? `correct ${summary.correct}` : null,
    summary.create > 0 ? `create ${summary.create} item${summary.create === 1 ? "" : "s"}` : null,
  ].filter(Boolean);
  return parts.length > 0 ? parts.join(" · ") : "Match every line first";
}
