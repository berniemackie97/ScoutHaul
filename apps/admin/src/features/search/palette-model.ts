import type { components } from "@overhaul/api-client";
import { navGroups, pageAt } from "@/components/layout/navigation";
import { allows, type Granted } from "@/lib/permissions";

export type SearchResults = components["schemas"]["SearchResultsDto"];
export type SearchHit = components["schemas"]["SearchHitDto"];

/**
 * Something the palette can do without asking the server: go to a page, or start a task.
 *
 * These are always available, so the palette is useful from the first keystroke — and before any —
 * rather than being an empty box that only wakes up at two characters.
 */
export type PaletteCommand = {
  id: string;
  label: string;
  section: "Actions" | "Go to";
  href: string;
  /** Other words someone might type for this. Matched, never shown. */
  keywords: string[];
};

/**
 * The tasks worth a shortcut: the ones done most, from the pages people are least likely to be on.
 * Every href here is a real route; adding one that is not fails the test that walks this list.
 */
export const ACTIONS: PaletteCommand[] = [
  { id: "action:add-item", label: "Add an inventory item", section: "Actions", href: "/inventory/add?mode=single", keywords: ["new item", "intake", "create"] },
  { id: "action:add-lot", label: "Add a lot of items", section: "Actions", href: "/inventory/add?mode=lot", keywords: ["haul", "pallet", "bulk", "several"] },
  { id: "action:add-bundle", label: "Create a bundle", section: "Actions", href: "/inventory/add?mode=bundle", keywords: ["combine", "set", "kit"] },
  { id: "action:record-sale", label: "Record a sale", section: "Actions", href: "/orders/new", keywords: ["sold", "new order", "sell"] },
  { id: "action:bundled-sale", label: "Record a bundled sale", section: "Actions", href: "/orders/bundled", keywords: ["sold together", "multiple items"] },
  { id: "action:record-purchase", label: "Record a purchase", section: "Actions", href: "/purchases", keywords: ["bought", "acquisition", "receipt", "lot"] },
  { id: "action:import", label: "Import marketplace exports", section: "Actions", href: "/imports", keywords: ["upload", "csv", "ebay report", "depop", "poshmark", "drop file"] },
  { id: "action:match-orders", label: "Match imported orders", section: "Actions", href: "/imports/orders", keywords: ["reconcile", "record sales", "unmatched"] },
  { id: "action:match-listings", label: "Match imported listings", section: "Actions", href: "/imports/listings", keywords: ["reconcile", "link listings", "unmatched"] },
  { id: "action:sync", label: "Scan marketplace sales and listings", section: "Actions", href: "/sync", keywords: ["sync", "scan ebay", "refresh sales", "update listings", "extension"] },
  { id: "action:connect-browser", label: "Connect a browser for syncing", section: "Actions", href: "/sync/browsers?connect=1", keywords: ["pair", "extension", "install", "firefox", "add-on"] },
];

export const NAVIGATION: PaletteCommand[] = navGroups.flatMap((group) =>
  group.items.map((item) => ({
    id: `nav:${item.href}`,
    label: item.name,
    section: "Go to" as const,
    href: item.href,
    keywords: [group.label, ...(item.keywords ?? [])],
  })),
);

export const ALL_COMMANDS: PaletteCommand[] = [...ACTIONS, ...NAVIGATION];

/** The commands whose page this person can open; a shortcut to a page that refuses them helps nobody. */
export function commandsFor(granted: Granted): PaletteCommand[] {
  return ALL_COMMANDS.filter((command) => allows(granted, pageAt(command.href.split("?")[0]!)?.requires));
}

/** Below this the server returns nothing; the palette shows commands alone rather than asking. */
export const MINIMUM_SEARCH_LENGTH = 2;

/**
 * Commands matching what has been typed, best first.
 *
 * A label that starts with the query beats one that merely contains it, which beats a keyword
 * match — so "or" puts Orders above "Record a purchase", and "sold" still finds Record a sale
 * without the word appearing anywhere on screen. Every word typed has to match somewhere, so
 * "record sale" narrows rather than widens.
 */
export function matchCommands(query: string, commands: PaletteCommand[] = ALL_COMMANDS): PaletteCommand[] {
  const words = normalize(query).split(" ").filter(Boolean);
  if (words.length === 0) return commands;

  return commands
    .map((command, index) => ({ command, index, score: scoreCommand(command, words) }))
    .filter((entry) => entry.score > 0)
    .sort((a, b) => b.score - a.score || a.index - b.index)
    .map((entry) => entry.command);
}

function scoreCommand(command: PaletteCommand, words: string[]): number {
  const label = normalize(command.label);
  const keywords = command.keywords.map(normalize);
  let score = 0;

  for (const word of words) {
    if (label.startsWith(word)) score += 4;
    else if (label.split(" ").some((part) => part.startsWith(word))) score += 3;
    else if (label.includes(word)) score += 2;
    else if (keywords.some((keyword) => keyword.includes(word))) score += 1;
    else return 0;
  }

  return score;
}

function normalize(value: string): string {
  return value.toLowerCase().replace(/\s+/g, " ").trim();
}

/** Where a search hit opens. Unknown kinds go nowhere rather than to a guessed URL. */
export function hrefForHit(hit: Pick<SearchHit, "kind" | "id">): string | null {
  if (!hit.id) return null;
  switch (hit.kind) {
    case "inventory":
      return `/inventory/${hit.id}`;
    case "order":
      return `/orders/${hit.id}`;
    case "customer":
      return `/customers/${hit.id}`;
    case "purchase":
      return `/purchases/${hit.id}`;
    default:
      return null;
  }
}

/**
 * The full list page for a kind, already searched — where "there are more" leads.
 *
 * Every one of these pages reads `q`, so the operator lands on the same results with paging rather
 * than on an unfiltered list they have to search again.
 */
export function seeAllHref(kind: string | null | undefined, query: string): string | null {
  const page = { inventory: "/inventory", order: "/orders", customer: "/customers", purchase: "/purchases" }[kind ?? ""];
  return page ? `${page}?q=${encodeURIComponent(query.trim())}` : null;
}

/**
 * Whether these results answer what is in the box right now.
 *
 * Typing fast, the answer to "la" can land after "lamp shade" was typed. Those results are real but
 * they are not the answer to the current question, so they are neither shown nor allowed to decide
 * that the current question has no matches.
 */
export function isAnswerFor(results: SearchResults | null, query: string): results is SearchResults {
  return results !== null && normalize(results.query ?? "") === normalize(query);
}

/** One selectable row, in the order the keyboard walks them. */
export type PaletteEntry =
  | { key: string; type: "command"; section: string; command: PaletteCommand }
  | { key: string; type: "hit"; section: string; hit: SearchHit; href: string }
  | { key: string; type: "see-all"; section: string; label: string; href: string };

/**
 * Everything the palette will show, flattened into the order arrow keys move through it.
 *
 * Records lead once there is a query: someone who typed an order number wants the order, not the
 * Orders page. Commands follow, then — with nothing typed yet — commands are all there is.
 */
export function buildEntries(query: string, results: SearchResults | null, granted: Granted = null): PaletteEntry[] {
  const entries: PaletteEntry[] = [];
  const typed = query.trim();

  if (typed.length >= MINIMUM_SEARCH_LENGTH && isAnswerFor(results, typed)) {
    for (const group of results.groups ?? []) {
      const section = group.label ?? "Results";
      for (const hit of group.hits ?? []) {
        const href = hrefForHit(hit);
        if (href) entries.push({ key: `hit:${hit.kind}:${hit.id}`, type: "hit", section, hit, href });
      }
      const more = group.truncated ? seeAllHref(group.kind, typed) : null;
      if (more) {
        entries.push({ key: `see-all:${group.kind}`, type: "see-all", section, label: `See all matching ${section.toLowerCase()}`, href: more });
      }
    }
  }

  for (const command of contiguousBySection(matchCommands(typed, commandsFor(granted)))) {
    entries.push({ key: command.id, type: "command", section: command.section, command });
  }

  return entries;
}

/**
 * Keeps each section in one piece while preserving the ranking inside it.
 *
 * Ranking runs across Actions and Go to together, so on its own it can interleave them — and a list
 * that says "Actions", then "Go to", then "Actions" again is two headers for one thing. The section
 * holding the best match comes first; everything else keeps its rank within its own section.
 */
function contiguousBySection(commands: PaletteCommand[]): PaletteCommand[] {
  const order: PaletteCommand["section"][] = [];
  const bySection = new Map<PaletteCommand["section"], PaletteCommand[]>();
  for (const command of commands) {
    if (!bySection.has(command.section)) {
      order.push(command.section);
      bySection.set(command.section, []);
    }
    bySection.get(command.section)!.push(command);
  }
  return order.flatMap((section) => bySection.get(section) ?? []);
}

/** Moves the selection, wrapping at both ends so the list never traps the cursor. */
export function stepIndex(current: number, delta: number, length: number): number {
  if (length === 0) return -1;
  if (current < 0) return delta > 0 ? 0 : length - 1;
  return (((current + delta) % length) + length) % length;
}
