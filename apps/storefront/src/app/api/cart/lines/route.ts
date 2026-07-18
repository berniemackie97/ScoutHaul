import { type NextRequest } from "next/server";
import { storefrontProxy } from "@/lib/storefront-proxy";

export async function POST(request: NextRequest) {
  const body = await request.json();
  return storefrontProxy(request, {
    method: "POST",
    path: "/api/storefront/cart/lines",
    body,
  });
}
