import { NextResponse } from "next/server";

// Public storefront — no auth gate. Customer auth (Supabase) is added with checkout/account.
export function middleware() {
  return NextResponse.next();
}

export const config = {
  matcher: [],
};
