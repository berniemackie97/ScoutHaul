import { beforeEach, describe, expect, it, vi } from "vitest";
import { permission } from "./permissions";

const session = vi.hoisted(() => ({ current: null as { permissions?: string[] } | null }));
vi.mock("@/lib/session", () => ({ getSession: async () => session.current }));

const { refusal } = await import("./page-access");

describe("a page's own check", () => {
  beforeEach(() => {
    session.current = { permissions: [permission.orders] };
  });

  it("refuses a page the role cannot open, before the page asks for anything", async () => {
    expect(await refusal("/suppliers")).not.toBeNull();
    expect(await refusal("/orders/new")).not.toBeNull();
  });

  it("lets through a page the role can open, and one open to every member", async () => {
    expect(await refusal("/orders")).toBeNull();
    expect(await refusal("/imports")).toBeNull();
  });

  it("refuses nothing when the session could not be read, so the page reports its own failure", async () => {
    session.current = null;
    expect(await refusal("/suppliers")).toBeNull();
  });
});
