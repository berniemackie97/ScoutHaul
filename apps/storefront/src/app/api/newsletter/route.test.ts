import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { POST } from "./route";

const post = (body: unknown) =>
  POST(new Request("http://localhost/api/newsletter", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  }) as never);

const fetchMock = vi.fn();

beforeEach(() => {
  vi.stubEnv("RESEND_API_KEY", "re_test");
  vi.stubGlobal("fetch", fetchMock);
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(new Response("{}", { status: 200 }));
  vi.spyOn(console, "error").mockImplementation(() => {});
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
});

describe("newsletter signup", () => {
  it("sends the address to the shop and reports success", async () => {
    const response = await post({ email: "buyer@example.com", source: "home" });

    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toEqual({ ok: true });

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe("https://api.resend.com/emails");
    const sent = JSON.parse((init as RequestInit).body as string);
    expect(sent.to).toEqual(["overhaultradingco@gmail.com"]);
    // Replying to the notification reaches the shopper.
    expect(sent.reply_to).toBe("buyer@example.com");
    expect(sent.html).toContain("buyer@example.com");
  });

  it("refuses an address that is not one", async () => {
    for (const email of ["", "   ", "nope", "a@b", "@example.com"]) {
      const response = await post({ email });
      expect(response.status).toBe(400);
    }
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses an absurdly long address rather than passing it on", async () => {
    const response = await post({ email: `${"a".repeat(250)}@example.com` });

    expect(response.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("swallows a honeypot submission without sending anything", async () => {
    // A bot cannot tell this apart from success, which is the point.
    const response = await post({ email: "bot@example.com", company: "Acme" });

    expect(response.status).toBe(200);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("says so when it cannot send, rather than claiming success", async () => {
    // The whole reason this route exists: the old form reported success unconditionally.
    fetchMock.mockResolvedValue(new Response("nope", { status: 422 }));

    const response = await post({ email: "buyer@example.com" });

    expect(response.status).toBe(502);
    await expect(response.json()).resolves.toHaveProperty("error");
  });

  it("does not claim success when the send throws", async () => {
    fetchMock.mockRejectedValue(new Error("network down"));

    const response = await post({ email: "buyer@example.com" });
    expect(response.status).toBe(502);
  });

  it("does not claim success when the API key is missing", async () => {
    vi.stubEnv("RESEND_API_KEY", "");

    const response = await post({ email: "buyer@example.com" });

    expect(response.status).toBe(503);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("rejects a body that is not JSON", async () => {
    const response = await POST(new Request("http://localhost/api/newsletter", {
      method: "POST",
      body: "not json",
    }) as never);

    expect(response.status).toBe(400);
  });

  it("escapes the address so it cannot inject markup into the notification", async () => {
    await post({ email: "a@b.co", source: "<img src=x onerror=alert(1)>" });

    const sent = JSON.parse((fetchMock.mock.calls[0]![1] as RequestInit).body as string);
    expect(sent.html).not.toContain("<img");
    expect(sent.html).toContain("&lt;img");
  });
});
