import { type NextRequest, NextResponse } from "next/server";
import { STORE } from "./store";

export const CART_COOKIE = "haulscout_cart";

type ProxyOptions = {
  method?: string;
  path: string;
  body?: unknown;
};

export async function storefrontProxy(request: NextRequest, options: ProxyOptions) {
  const token = request.cookies.get(CART_COOKIE)?.value;
  const headers = new Headers({
    Accept: "application/json",
    "X-HaulScout-Store": STORE.domain,
  });

  if (token) {
    headers.set("X-HaulScout-Cart", token);
  }

  const init: RequestInit = {
    method: options.method ?? "GET",
    headers,
    cache: "no-store",
  };

  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
    init.body = JSON.stringify(options.body);
  }

  const upstream = await fetch(`${STORE.apiBaseUrl}${options.path}`, init);
  const text = await upstream.text();
  const contentType = upstream.headers.get("Content-Type") ?? "application/json";
  const response = new NextResponse(text, {
    status: upstream.status,
    headers: { "Content-Type": contentType },
  });

  if (upstream.ok && contentType.includes("application/json") && text.length > 0) {
    try {
      const payload = JSON.parse(text) as { cartToken?: string };
      if (payload.cartToken) {
        response.cookies.set(CART_COOKIE, payload.cartToken, {
          httpOnly: true,
          sameSite: "lax",
          secure: process.env.NODE_ENV === "production",
          path: "/",
          maxAge: 60 * 60 * 24 * 30,
        });
      }
    } catch {
      // Non-object JSON response. Nothing to persist.
    }
  }

  return response;
}

export async function storefrontServerJson<T>(path: string): Promise<T | null> {
  const response = await fetch(`${STORE.apiBaseUrl}${path}`, {
    headers: {
      Accept: "application/json",
      "X-HaulScout-Store": STORE.domain,
    },
    cache: "no-store",
  });
  if (!response.ok) return null;
  return (await response.json()) as T;
}
