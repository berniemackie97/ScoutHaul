import { NextResponse } from "next/server";
import { getApiClient } from "@/lib/api";

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The state of a pairing, polled by the Connect a browser panel while the code is on screen. */
export async function GET(_request: Request, { params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  if (!GUID.test(id)) {
    return NextResponse.json({ detail: "Not a pairing id." }, { status: 400 });
  }

  const api = await getApiClient();
  const { data, error, response } = await api.GET("/api/sync/pairings/{pairingId}", { params: { path: { pairingId: id } } });
  return NextResponse.json(data ?? error ?? {}, { status: response.status });
}
