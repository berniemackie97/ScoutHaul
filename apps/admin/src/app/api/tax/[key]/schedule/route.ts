import { NextResponse } from "next/server";
import { getApiAccessToken, getApiBaseUrl } from "@/lib/api";

export async function GET(_request: Request, { params }: { params: Promise<{ key: string }> }) {
  const token = await getApiAccessToken();
  if (!token) return NextResponse.json({ error: "Unauthorized" }, { status: 401 });

  const { key } = await params;
  if (!/^\d{8}-\d{8}$/.test(key)) return NextResponse.json({ error: "No such period." }, { status: 404 });

  const response = await fetch(`${getApiBaseUrl()}/api/sales-tax/periods/${key}/schedule.csv`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });
  if (!response.ok) {
    return NextResponse.json({ error: (await response.text()) || "The schedule could not be exported." }, { status: response.status });
  }

  return new NextResponse(await response.arrayBuffer(), {
    status: 200,
    headers: {
      "Content-Type": response.headers.get("Content-Type") ?? "text/csv; charset=utf-8",
      "Content-Disposition": response.headers.get("Content-Disposition") ?? `attachment; filename=fl-dr15-${key}.csv`,
      "Cache-Control": "private, no-store",
    },
  });
}
