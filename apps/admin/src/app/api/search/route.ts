import { NextResponse } from "next/server";
import { getApiAccessToken, getApiClient } from "@/lib/api";

/**
 * The command palette's search, relayed to the API with the operator's session.
 *
 * The palette is a client component and the session token lives in a server-side cookie, so it
 * cannot call the API itself. This passes the query through and the answer back — nothing more.
 * The caller's abort signal is forwarded, so a query superseded by the next keystroke is dropped
 * all the way to the API rather than finishing just to be thrown away.
 */
export async function GET(request: Request) {
  if (!(await getApiAccessToken())) {
    return NextResponse.json(
      { error: "Your session has ended. Sign in again to search." },
      { status: 401, headers: { "Cache-Control": "private, no-store" } },
    );
  }

  const q = new URL(request.url).searchParams.get("q") ?? "";
  const api = await getApiClient();

  try {
    const { data, response } = await api.GET("/api/search", {
      params: { query: { q } },
      signal: request.signal,
    });

    if (!data) {
      return NextResponse.json(
        { error: "Search is unavailable right now." },
        { status: response.status >= 400 ? response.status : 502, headers: { "Cache-Control": "private, no-store" } },
      );
    }

    return NextResponse.json(data, { headers: { "Cache-Control": "private, no-store" } });
  } catch (error) {
    // Superseded by a newer query — the client already moved on and will not read this.
    if (request.signal.aborted) return new NextResponse(null, { status: 499 });
    throw error;
  }
}
