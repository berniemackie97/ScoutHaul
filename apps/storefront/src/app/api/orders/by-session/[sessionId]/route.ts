import { type NextRequest } from "next/server";
import { storefrontProxy } from "@/lib/storefront-proxy";

export async function GET(
  request: NextRequest,
  context: { params: Promise<{ sessionId: string }> },
) {
  const { sessionId } = await context.params;
  return storefrontProxy(request, {
    path: `/api/storefront/orders/by-session/${encodeURIComponent(sessionId)}`,
  });
}
