import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { pageAt } from "./navigation";

const dashboard = path.resolve(__dirname, "../../app/(dashboard)");

function pages(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(directory, entry.name);
    return entry.isDirectory() ? pages(full) : entry.name === "page.tsx" ? [full] : [];
  });
}

/** "/orders/[id]/page.tsx" → "/orders/x": a real address under the route. */
function routeOf(file: string): string {
  const segments = path.relative(dashboard, path.dirname(file)).split(path.sep).filter(Boolean);
  return `/${segments.map((segment) => (segment.startsWith("[") ? "x" : segment)).join("/")}`;
}

describe("every page a role can be refused", () => {
  it("checks the role before it asks the API for anything", () => {
    for (const file of pages(dashboard)) {
      const route = routeOf(file);
      const rule = pageAt(route);
      if (!rule?.requires) continue;

      const source = readFileSync(file, "utf8");
      const guarded = /await refusal\("([^"]+)"\)/.exec(source);
      expect({ route, guarded: guarded !== null }).toEqual({ route, guarded: true });
      // The route it names must be judged by the same rule as the page itself.
      expect({ route, rule: pageAt(guarded![1]!) }).toEqual({ route, rule });
      const firstRequest = source.indexOf("getApiClient(");
      expect({ route, guardFirst: firstRequest < 0 || guarded!.index < firstRequest }).toEqual({ route, guardFirst: true });
    }
  });
});
