import { existsSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { commandsFor } from "@/features/search/palette-model";
import { permission } from "@/lib/permissions";
import { navGroups, pageAt, subPages, visibleGroups } from "./navigation";

const dashboard = path.resolve(__dirname, "../../app/(dashboard)");
const routeExists = (href: string) => existsSync(path.join(dashboard, ...(href === "/" ? [] : href.slice(1).split("/")), "page.tsx"));

describe("what a role is offered", () => {
  it("names only pages that exist, so a rule cannot guard a route that was renamed away", () => {
    for (const page of [...subPages, ...navGroups.flatMap((group) => group.items)]) {
      expect({ page: page.href, exists: routeExists(page.href) }).toEqual({ page: page.href, exists: true });
    }
  });

  it("judges a page by its most specific rule", () => {
    expect(pageAt("/orders/new")?.name).toBe("Record a sale");
    expect(pageAt("/orders/3f1c")?.name).toBe("Orders");
    expect(pageAt("/imports/runs/9a")?.requires).toBeUndefined();
    expect(pageAt("/")?.name).toBe("Command Center");
  });

  it("leaves out what the role cannot open, and a group with nothing left in it", () => {
    const groups = visibleGroups(new Set([permission.orders]));

    expect(groups.flatMap((group) => group.items.map((item) => item.name))).toEqual(["Command Center", "Work", "Orders", "Offers", "Customers", "Imports"]);
    expect(groups.map((group) => group.label)).not.toContain("Operations");
  });

  it("shows every page when the session could not be read, rather than an empty console", () => {
    expect(visibleGroups(null)).toEqual(navGroups);
  });

  it("offers Settings to anyone who can use one part of it", () => {
    expect(visibleGroups(new Set([permission.members])).flatMap((group) => group.items.map((item) => item.name))).toContain("Settings");
  });

  it("gives no shortcut to a page the role cannot open", () => {
    const labels = commandsFor(new Set([permission.orders, permission.listings])).map((command) => command.label);

    // Recording a sale needs money as well as listings; a purchase needs money alone.
    expect(labels).not.toContain("Record a sale");
    expect(labels).not.toContain("Record a purchase");
    expect(labels).toContain("Scan marketplace sales and listings");
    expect(labels).toContain("Orders");
  });
});
