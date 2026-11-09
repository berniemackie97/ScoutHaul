import { getApiAccessToken, getApiBaseUrl } from "@/lib/api";

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function GET(request: Request, { params }: { params: Promise<{ itemId: string; photoId: string; kind: string }> }) {
  const { itemId, photoId, kind } = await params;
  if (!guid.test(itemId) || !guid.test(photoId) || (kind !== "thumbnail" && kind !== "original")) {
    return new Response(null, { status: 404, headers: { "Cache-Control": "private, no-store" } });
  }

  const token = await getApiAccessToken();
  if (!token) return new Response(null, { status: 401, headers: { "Cache-Control": "private, no-store" } });

  const result = await fetch(`${getApiBaseUrl()}/api/inventory/${itemId}/photos/${photoId}/${kind}`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
    signal: request.signal,
  });
  if (!result.ok) {
    return new Response(null, { status: result.status, headers: { "Cache-Control": "private, no-store" } });
  }

  return new Response(result.body, {
    headers: {
      "Content-Type": result.headers.get("Content-Type") ?? "application/octet-stream",
      "Cache-Control": "private, no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
