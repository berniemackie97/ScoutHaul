import { type NextRequest, NextResponse } from "next/server";
import { storefrontProxy } from "@/lib/storefront-proxy";
import { createSupabaseServerClient } from "@/lib/supabase/server";

export async function POST(
  request: NextRequest,
  { params }: { params: Promise<{ offerId: string }> },
) {
  const supabase = await createSupabaseServerClient();
  const {
    data: { session },
  } = await supabase.auth.getSession();

  if (!session?.access_token) {
    return NextResponse.json({ detail: "Sign in to use this offer." }, { status: 401 });
  }

  const { offerId } = await params;
  return storefrontProxy(request, {
    method: "POST",
    path: `/api/storefront/cart/offers/${offerId}/line`,
    accessToken: session.access_token,
  });
}
