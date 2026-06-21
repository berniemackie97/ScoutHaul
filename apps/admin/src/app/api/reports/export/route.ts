import { NextResponse } from "next/server";
import { getApiAccessToken, getApiBaseUrl } from "@/lib/api";

export async function GET(request: Request) {
  const token = await getApiAccessToken();
  if (!token) return NextResponse.json({ error: "Unauthorized" }, { status: 401 });

  const requestUrl = new URL(request.url);
  const query = new URLSearchParams();
  for (const key of ["from", "to"]) {
    const value = requestUrl.searchParams.get(key);
    if (value) query.set(key, value);
  }

  const response = await fetch(`${getApiBaseUrl()}/api/reports/overview.csv?${query}`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });
  if (!response.ok) {
    return NextResponse.json({ error: await response.text() || "Report export failed." }, { status: response.status });
  }

  return new NextResponse(await response.arrayBuffer(), {
    status: 200,
    headers: {
      "Content-Type": response.headers.get("Content-Type") ?? "text/csv; charset=utf-8",
      "Content-Disposition": response.headers.get("Content-Disposition") ?? "attachment; filename=haulscout-report.csv",
      "Cache-Control": "private, no-store",
    },
  });
}
