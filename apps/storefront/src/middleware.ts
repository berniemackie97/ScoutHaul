import { updateSession } from "@/lib/supabase/middleware";

// Public storefront: refresh buyer auth cookies without gating browse/cart/checkout pages.
export const middleware = updateSession;

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp)$).*)"],
};
