import { type NextRequest } from "next/server";
import { storefrontProxy } from "@/lib/storefront-proxy";

export async function POST(request: NextRequest) {
  return storefrontProxy(request, {
    method: "POST",
    path: "/api/storefront/checkout",
  });
}
