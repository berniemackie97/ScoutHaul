import { existsSync, readdirSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

const appDir = path.resolve(__dirname);

/**
 * The console had no error boundary anywhere, so one thrown error in one page replaced the entire
 * document — sidebar included — and left the operator with no way out but the browser's back button.
 *
 * These assert the boundaries are still there. Deleting one is a silent change: nothing fails to
 * compile, nothing looks different, and the damage only shows up the next time an API call fails.
 */
describe("error boundaries", () => {
  it("catches a failure inside the console without taking the shell down", () => {
    // Must sit inside (dashboard), beside the layout that renders the shell. At the root it would
    // still replace the navigation.
    expect(existsSync(path.join(appDir, "(dashboard)", "error.tsx"))).toBe(true);
    expect(existsSync(path.join(appDir, "(dashboard)", "layout.tsx"))).toBe(true);
  });

  it("catches a failure outside the console", () => {
    // Sign-in and the auth callback live here.
    expect(existsSync(path.join(appDir, "error.tsx"))).toBe(true);
  });

  it("catches a failure in the root layout itself", () => {
    // The one place the other two cannot reach.
    expect(existsSync(path.join(appDir, "global-error.tsx"))).toBe(true);
  });

  it("shows a missing record inside the console rather than a bare 404", () => {
    // notFound() is called on four detail pages.
    expect(existsSync(path.join(appDir, "(dashboard)", "not-found.tsx"))).toBe(true);
  });

  it("shows something while a page is fetching", () => {
    expect(existsSync(path.join(appDir, "(dashboard)", "loading.tsx"))).toBe(true);
  });

  it("keeps every route group under the console covered by one of them", () => {
    // A new route group with its own layout would need its own boundary; anything else inherits
    // (dashboard)'s. This fails when someone adds one and forgets.
    const groups = readdirSync(appDir, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && entry.name.startsWith("("))
      .map((entry) => entry.name);

    for (const group of groups) {
      expect(existsSync(path.join(appDir, group, "error.tsx"))).toBe(true);
    }
  });
});
