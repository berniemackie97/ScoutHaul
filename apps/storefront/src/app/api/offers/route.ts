import { type NextRequest, NextResponse } from "next/server";
import { storefrontProxy } from "@/lib/storefront-proxy";
import { createSupabaseServerClient } from "@/lib/supabase/server";

export async function GET(request: NextRequest) {
  const token = await customerAccessToken();
  if (!token) {
    return NextResponse.json({ detail: "Sign in to view offers." }, { status: 401 });
  }

  return storefrontProxy(request, {
    method: "GET",
    path: "/api/storefront/offers",
    accessToken: token,
  });
}

export async function POST(request: NextRequest) {
  const token = await customerAccessToken();
  if (!token) {
    return NextResponse.json({ detail: "Sign in to make an offer." }, { status: 401 });
  }

  const body = await request.json();
  return storefrontProxy(request, {
    method: "POST",
    path: "/api/storefront/offers",
    body,
    accessToken: token,
  });
}

async function customerAccessToken() {
  try {
    const supabase = await createSupabaseServerClient();
    const {
      data: { session },
    } = await supabase.auth.getSession();
    return session?.access_token ?? null;
  } catch {
    return null;
  }
}
