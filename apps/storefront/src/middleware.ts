import { updateSession } from "@/lib/supabase/middleware";

// Public storefront: refresh buyer auth cookies without gating browse/cart/checkout pages.
export const middleware = updateSession;

// Auth refresh runs on real pages only — never on the app's own /api proxy routes (they issue their own
// Set-Cookie for the cart and manage auth per-request; middleware re-wrapping their response can drop those
// cookies) and never on static assets. This keeps anonymous browse fast and the cart cookie intact.
export const config = {
  matcher: ["/((?!api|_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico)$).*)"],
};
