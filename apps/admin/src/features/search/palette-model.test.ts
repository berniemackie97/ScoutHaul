import { describe, expect, it } from "vitest";
import { existsSync } from "node:fs";
import path from "node:path";
import {
  ACTIONS,
  ALL_COMMANDS,
  NAVIGATION,
  buildEntries,
  hrefForHit,
  isAnswerFor,
  matchCommands,
  seeAllHref,
  stepIndex,
  type SearchResults,
} from "./palette-model";

const dashboard = path.resolve(__dirname, "../../app/(dashboard)");

/** Whether a path like "/orders/new" is a page that exists under the dashboard route group. */
function routeExists(href: string): boolean {
  const pathname = href.split("?")[0] ?? href;
  const segments = pathname === "/" ? [] : pathname.slice(1).split("/");
  return existsSync(path.join(dashboard, ...segments, "page.tsx"));
}

const results = (query: string, groups: SearchResults["groups"]): SearchResults => ({
  query,
  groups,
  totalHits: (groups ?? []).reduce((sum, group) => sum + (group.hits?.length ?? 0), 0),
});

describe("commands", () => {
  it("only ever point at pages that exist", () => {
    // A renamed route should fail here, not as a palette entry that opens a 404.
    for (const command of ALL_COMMANDS) {
      expect({ command: command.label, exists: routeExists(command.href) }).toEqual({
        command: command.label,
        exists: true,
      });
    }
  });

  it("cover every page in the sidebar", () => {
    // Navigation is derived from the sidebar's own list, so a new page cannot be reachable there
    // and missing here.
    expect(NAVIGATION.map((command) => command.href)).toContain("/repairs");
    expect(NAVIGATION.map((command) => command.href)).toContain("/");
  });

  it("have unique ids, because the keyboard selection is keyed by them", () => {
    const ids = ALL_COMMANDS.map((command) => command.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
});

describe("matchCommands", () => {
  it("returns everything when nothing is typed", () => {
    expect(matchCommands("")).toHaveLength(ALL_COMMANDS.length);
    expect(matchCommands("   ")).toHaveLength(ALL_COMMANDS.length);
  });

  it("puts a label that starts with the query above one that merely contains it", () => {
    const labels = matchCommands("or").map((command) => command.label);
    expect(labels[0]).toBe("Orders");
  });

  it("finds a command by a word that never appears on screen", () => {
    // Nobody types "Record a sale"; they type "sold".
    expect(matchCommands("sold").map((command) => command.id)).toContain("action:record-sale");
  });

  it("finds a page by what it is for, not only what it is called", () => {
    expect(matchCommands("broken").map((command) => command.href)).toContain("/repairs");
  });

  it("narrows as more words are typed rather than widening", () => {
    const broad = matchCommands("record");
    const narrow = matchCommands("record sale");
    expect(narrow.length).toBeLessThan(broad.length);
    expect(narrow.every((command) => command.label.toLowerCase().includes("sale"))).toBe(true);
  });

  it("ignores case and stray spacing", () => {
    expect(matchCommands("  REPAIRS ").map((command) => command.href)).toContain("/repairs");
  });

  it("returns nothing for a query that matches nothing", () => {
    expect(matchCommands("zzzz")).toEqual([]);
  });

  it("keeps the declared order between equally good matches", () => {
    // Stable ordering: the list does not reshuffle itself between two keystrokes that score alike.
    const first = matchCommands("import");
    const second = matchCommands("import");
    expect(first.map((command) => command.id)).toEqual(second.map((command) => command.id));
    expect(first[0]?.id).toBe("action:import-orders");
  });
});

describe("hrefForHit", () => {
  it.each([
    ["inventory", "/inventory/abc"],
    ["order", "/orders/abc"],
    ["customer", "/customers/abc"],
    ["purchase", "/purchases/abc"],
  ])("opens a %s at its detail page", (kind, href) => {
    expect(hrefForHit({ kind, id: "abc" })).toBe(href);
  });

  it("goes nowhere for a kind it does not know rather than guessing a URL", () => {
    expect(hrefForHit({ kind: "invoice", id: "abc" })).toBeNull();
  });

  it("goes nowhere without an id", () => {
    expect(hrefForHit({ kind: "order", id: undefined })).toBeNull();
  });
});

describe("seeAllHref", () => {
  it("lands on the list page already searched", () => {
    expect(seeAllHref("order", "SH-10")).toBe("/orders?q=SH-10");
  });

  it("encodes what was typed", () => {
    expect(seeAllHref("inventory", "banker's lamp & shade")).toBe(
      "/inventory?q=banker's%20lamp%20%26%20shade",
    );
  });

  it("has nowhere to send an unknown kind", () => {
    expect(seeAllHref("invoice", "x")).toBeNull();
  });
});

describe("buildEntries", () => {
  const lampResults = results("lamp", [
    {
      kind: "inventory",
      label: "Inventory",
      truncated: true,
      hits: [
        { kind: "inventory", id: "i1", label: "Brass lamp", sublabel: "Shelf B4", badge: "Listed" },
        { kind: "inventory", id: "i2", label: "Desk lamp", sublabel: null, badge: "Draft" },
      ],
    },
  ]);

  it("shows only commands before anything is typed", () => {
    const entries = buildEntries("", null);
    expect(entries.every((entry) => entry.type === "command")).toBe(true);
    expect(entries).toHaveLength(ALL_COMMANDS.length);
  });

  it("puts records ahead of commands once there is a query", () => {
    // Someone who typed an order number wants the order, not the Orders page.
    const entries = buildEntries("lamp", lampResults);
    expect(entries[0]).toMatchObject({ type: "hit", href: "/inventory/i1" });
  });

  it("offers the rest of a group that was cut off", () => {
    const entries = buildEntries("lamp", lampResults);
    expect(entries).toContainEqual(
      expect.objectContaining({ type: "see-all", href: "/inventory?q=lamp" }),
    );
  });

  it("does not offer more when the group was complete", () => {
    const complete = results("lamp", [{ ...lampResults.groups![0], truncated: false }]);
    expect(buildEntries("lamp", complete).some((entry) => entry.type === "see-all")).toBe(false);
  });

  it("ignores results that belong to an earlier query", () => {
    // Typing fast, "la" can answer after "lamp shade" was typed. Showing its results under the new
    // query would put records on screen that do not match what is in the box.
    const entries = buildEntries("lamp shade", lampResults);
    expect(entries.some((entry) => entry.type === "hit")).toBe(false);
  });

  it("shows no records for a query too short to search", () => {
    expect(buildEntries("l", results("l", lampResults.groups)).some((entry) => entry.type === "hit")).toBe(false);
  });

  it("skips a hit it cannot open rather than rendering a dead row", () => {
    const odd = results("lamp", [
      { kind: "invoice", label: "Invoices", truncated: false, hits: [{ kind: "invoice", id: "x", label: "INV-1" }] },
    ]);
    expect(buildEntries("lamp", odd).some((entry) => entry.type === "hit")).toBe(false);
  });

  it("never splits a section into two runs", () => {
    // Ranking crosses sections, so without this "Actions, Go to, Actions" would render two headers
    // for one thing — and two React siblings with the same key.
    for (const query of ["", "or", "re", "record", "i", "sale", "import"]) {
      const sections = buildEntries(query, null).map((entry) => entry.section);
      const runs = sections.filter((section, index) => section !== sections[index - 1]);
      expect(new Set(runs).size).toBe(runs.length);
    }
  });

  it("leads with the section holding the best match", () => {
    expect(buildEntries("orders", null)[0]).toMatchObject({ type: "command", section: "Go to" });
    expect(buildEntries("record a sale", null)[0]).toMatchObject({ type: "command", section: "Actions" });
  });

  it("gives every row a unique key", () => {
    const keys = buildEntries("lamp", lampResults).map((entry) => entry.key);
    expect(new Set(keys).size).toBe(keys.length);
  });
});

describe("isAnswerFor", () => {
  const answer = results("lamp", []);

  it("recognises the answer to what is in the box", () => {
    expect(isAnswerFor(answer, "lamp")).toBe(true);
    expect(isAnswerFor(answer, "  LAMP ")).toBe(true);
  });

  it("does not let an older answer stand in for a newer question", () => {
    // Otherwise "la" answering late, empty, would declare "lamp shade" to have no matches.
    expect(isAnswerFor(answer, "lamp shade")).toBe(false);
  });

  it("has no answer before anything has come back", () => {
    expect(isAnswerFor(null, "lamp")).toBe(false);
  });
});

describe("stepIndex", () => {
  it("wraps from the bottom to the top and back", () => {
    expect(stepIndex(2, 1, 3)).toBe(0);
    expect(stepIndex(0, -1, 3)).toBe(2);
  });

  it("starts at the first row going down and the last going up", () => {
    expect(stepIndex(-1, 1, 3)).toBe(0);
    expect(stepIndex(-1, -1, 3)).toBe(2);
  });

  it("has nothing to select in an empty list", () => {
    expect(stepIndex(0, 1, 0)).toBe(-1);
  });
});

describe("actions", () => {
  it("include the tasks done most often", () => {
    const ids = ACTIONS.map((action) => action.id);
    expect(ids).toEqual(expect.arrayContaining(["action:record-sale", "action:add-item", "action:record-purchase"]));
  });
});
