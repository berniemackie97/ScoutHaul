import { NextResponse } from "next/server";
import { getApiAccessToken, getApiBaseUrl } from "@/lib/api";

/** The exact file an import read, downloaded with the operator's session. */
export async function GET(_request: Request, { params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const token = await getApiAccessToken();
  if (!token) return NextResponse.json({ detail: "Your session has ended. Sign in again." }, { status: 401 });

  // The id comes from the URL; only a GUID may be forwarded into the API path.
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) {
    return NextResponse.json({ detail: "That import was not found." }, { status: 404 });
  }

  const response = await fetch(`${getApiBaseUrl()}/api/imports/runs/${id}/file`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (!response.ok) {
    return new NextResponse(await response.text(), {
      status: response.status,
      headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
    });
  }

  return new NextResponse(response.body, {
    status: 200,
    headers: {
      "Content-Type": response.headers.get("Content-Type") ?? "application/octet-stream",
      "Content-Disposition": response.headers.get("Content-Disposition") ?? "attachment",
      "Cache-Control": "private, no-store",
    },
  });
}
