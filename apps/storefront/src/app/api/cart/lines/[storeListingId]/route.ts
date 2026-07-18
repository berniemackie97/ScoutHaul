import { type NextRequest } from "next/server";
import { storefrontProxy } from "@/lib/storefront-proxy";

export async function DELETE(
  request: NextRequest,
  context: { params: Promise<{ storeListingId: string }> },
) {
  const { storeListingId } = await context.params;
  return storefrontProxy(request, {
    method: "DELETE",
    path: `/api/storefront/cart/lines/${storeListingId}`,
  });
}
