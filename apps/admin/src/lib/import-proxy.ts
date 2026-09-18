import { NextResponse } from "next/server";
import { getApiAccessToken, getApiBaseUrl } from "@/lib/api";

const NO_STORE = { "Cache-Control": "private, no-store" };

/**
 * Relays dropped files to the API with the operator's session.
 *
 * The drop zone is a client component and the session token lives in a server-side cookie, so the
 * files pass through here. Only the files are forwarded — nothing else from the browser's form —
 * and the API's own status and body come back unchanged, so its refusals reach the operator as
 * written rather than as a generic failure.
 */
export async function forwardFiles(request: Request, apiPath: string): Promise<NextResponse> {
  const token = await getApiAccessToken();
  if (!token) {
    return NextResponse.json({ detail: "Your session has ended. Sign in again to import." }, { status: 401, headers: NO_STORE });
  }

  let incoming: FormData;
  try {
    incoming = await request.formData();
  } catch {
    return NextResponse.json({ detail: "The upload could not be read. Try choosing the files again." }, { status: 400, headers: NO_STORE });
  }

  const outgoing = new FormData();
  for (const entry of incoming.getAll("files")) {
    if (entry instanceof File) outgoing.append("files", entry, entry.name);
  }

  const response = await fetch(`${getApiBaseUrl()}${apiPath}`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    body: outgoing,
    cache: "no-store",
  });

  const body = await response.text();
  return new NextResponse(body, {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json", ...NO_STORE },
  });
}
