import { NextResponse } from "next/server";
import { getApiClient } from "@/lib/api";

/** Items a sale can be recorded against, matching what was typed — relayed with the operator's session. */
export async function GET(request: Request) {
  const search = new URL(request.url).searchParams.get("q")?.trim() ?? "";
  const api = await getApiClient();
  const { data, error, response } = await api.GET("/api/sales/candidates", {
    params: { query: { search: search || undefined, take: 12 } },
    signal: request.signal,
  });
  if (!data) {
    return NextResponse.json(error ?? { detail: "Inventory could not be searched." }, { status: response.status });
  }

  return NextResponse.json(data);
}
