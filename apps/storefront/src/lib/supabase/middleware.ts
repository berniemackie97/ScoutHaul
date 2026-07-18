import { createServerClient, type CookieOptions } from "@supabase/ssr";
import { NextResponse, type NextRequest } from "next/server";

/**
 * Best-effort refresh of the buyer's Supabase auth cookies on each request.
 *
 * This is a PUBLIC storefront: browsing, the cart, and checkout are all anonymous, so this must never gate or crash
 * a page. Two failure modes are handled explicitly, because either one used to 500 the entire site (the matcher runs
 * on every route):
 *   1. Customer auth isn't configured (env vars absent) — skip the refresh, serve the page anonymously.
 *   2. The refresh throws (invalid config, Supabase unreachable) — swallow it, serve the page anonymously.
 * A logged-out shopper is the correct fallback for a store; a 500 is never acceptable.
 */
export async function updateSession(request: NextRequest) {
  let response = NextResponse.next({ request });

  const url = process.env.NEXT_PUBLIC_SUPABASE_URL;
  const anonKey = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY;
  if (!url || !anonKey) {
    return response;
  }

  try {
    const supabase = createServerClient(url, anonKey, {
      cookies: {
        getAll() {
          return request.cookies.getAll();
        },
        setAll(cookiesToSet: { name: string; value: string; options: CookieOptions }[]) {
          cookiesToSet.forEach(({ name, value }) => request.cookies.set(name, value));
          response = NextResponse.next({ request });
          cookiesToSet.forEach(({ name, value, options }) => response.cookies.set(name, value, options));
        },
      },
    });

    await supabase.auth.getUser();
  } catch {
    // Auth refresh is optional here; a failure must not take the storefront down. Serve the page anonymously.
    return NextResponse.next({ request });
  }

  return response;
}
