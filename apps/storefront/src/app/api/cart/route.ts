import { type NextRequest } from "next/server";
import { storefrontProxy } from "@/lib/storefront-proxy";

export async function GET(request: NextRequest) {
  return storefrontProxy(request, { path: "/api/storefront/cart" });
}
