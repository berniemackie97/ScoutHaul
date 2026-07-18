import { NextResponse } from "next/server";
import { createSupabaseServerClient } from "@/lib/supabase/server";

export async function GET(request: Request) {
  const requestUrl = new URL(request.url);
  const code = requestUrl.searchParams.get("code");
  const next = requestUrl.searchParams.get("next");
  const safeNext = next?.startsWith("/") && !next.startsWith("//") ? next : "/account";

  if (!code) {
    return NextResponse.redirect(new URL("/sign-in?error=callback", requestUrl.origin));
  }

  let error: unknown;
  try {
    const supabase = await createSupabaseServerClient();
    const result = await supabase.auth.exchangeCodeForSession(code);
    error = result.error;
  } catch {
    error = true;
  }

  if (error) {
    return NextResponse.redirect(new URL("/sign-in?error=callback", requestUrl.origin));
  }

  return NextResponse.redirect(new URL(safeNext, requestUrl.origin));
}
