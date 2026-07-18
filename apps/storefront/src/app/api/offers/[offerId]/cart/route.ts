import { type NextRequest, NextResponse } from "next/server";
import { storefrontProxy } from "@/lib/storefront-proxy";
import { createSupabaseServerClient } from "@/lib/supabase/server";

export async function POST(
  request: NextRequest,
  { params }: { params: Promise<{ offerId: string }> },
) {
  let sessionAccessToken: string | undefined;
  try {
    const supabase = await createSupabaseServerClient();
    const {
      data: { session },
    } = await supabase.auth.getSession();
    sessionAccessToken = session?.access_token;
  } catch {
    return NextResponse.json({ detail: "Customer accounts are not configured for this storefront." }, { status: 503 });
  }

  if (!sessionAccessToken) {
    return NextResponse.json({ detail: "Sign in to use this offer." }, { status: 401 });
  }

  const { offerId } = await params;
  return storefrontProxy(request, {
    method: "POST",
    path: `/api/storefront/cart/offers/${offerId}/line`,
    accessToken: sessionAccessToken,
  });
}
