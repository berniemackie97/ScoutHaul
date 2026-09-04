import { type NextRequest, NextResponse } from "next/server";

/**
 * Consignment / repair request intake. Emails the submission (with photos attached) to the shop inbox via Resend.
 * No secret is stored in code: RESEND_API_KEY comes from the environment. The request is captured from anonymous
 * public traffic, so everything is validated and bounded here.
 */

const TO = process.env.RESEND_TO ?? "overhaultradingco@gmail.com";
// Until a domain is verified in Resend, sends must come from the shared onboarding sender.
const FROM = process.env.RESEND_FROM ?? "Overhaul Trading <onboarding@resend.dev>";

const MAX_PHOTOS = 6;
const MAX_PHOTO_BYTES = 5 * 1024 * 1024; // 5 MB per photo, post client-side compression
const MAX_FIELD_LEN = 5000;

type IncomingPhoto = { name?: string; dataUrl?: string };
type IncomingBody = {
  kind?: string;
  item?: Record<string, string>;
  contact?: { name?: string; email?: string; phone?: string };
  photos?: IncomingPhoto[];
  // Honeypot — real users never see or fill this.
  company?: string;
};

function isEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

/** "data:image/jpeg;base64,AAAA" -> { filename, contentType, base64 } (base64 only, as Resend wants). */
function parseDataUrl(dataUrl: string, fallbackName: string) {
  const match = /^data:([^;]+);base64,(.+)$/s.exec(dataUrl);
  if (!match) return null;
  const [, contentType, base64] = match;
  if (!contentType || !base64 || !contentType.startsWith("image/")) return null;
  const approxBytes = Math.ceil((base64.length * 3) / 4);
  if (approxBytes > MAX_PHOTO_BYTES) return null;
  const ext = contentType.split("/")[1]?.replace("jpeg", "jpg") ?? "jpg";
  const safeName = fallbackName.replace(/[^\w.-]+/g, "_").slice(0, 60) || `photo.${ext}`;
  return { filename: safeName.includes(".") ? safeName : `${safeName}.${ext}`, base64 };
}

export async function POST(request: NextRequest) {
  let body: IncomingBody;
  try {
    body = (await request.json()) as IncomingBody;
  } catch {
    return NextResponse.json({ error: "Invalid request." }, { status: 400 });
  }

  // Honeypot: pretend success, send nothing.
  if (body.company) return NextResponse.json({ ok: true });

  const kind = body.kind === "repair" ? "repair" : body.kind === "consignment" ? "consignment" : null;
  if (!kind) return NextResponse.json({ error: "Unknown request type." }, { status: 400 });

  const name = (body.contact?.name ?? "").trim();
  const email = (body.contact?.email ?? "").trim();
  const phone = (body.contact?.phone ?? "").trim();
  if (!name || !isEmail(email)) {
    return NextResponse.json({ error: "A name and a valid email are required." }, { status: 400 });
  }

  const item = body.item ?? {};
  const itemEntries = Object.entries(item)
    .map(([key, value]) => [key, String(value ?? "").trim()] as const)
    .filter(([, value]) => value.length > 0 && value.length <= MAX_FIELD_LEN);
  if (itemEntries.length === 0) {
    return NextResponse.json({ error: "Please describe the item." }, { status: 400 });
  }

  const photos = Array.isArray(body.photos) ? body.photos.slice(0, MAX_PHOTOS) : [];
  const attachments = photos
    .map((photo, index) => (photo?.dataUrl ? parseDataUrl(photo.dataUrl, photo.name ?? `photo-${index + 1}`) : null))
    .filter((attachment): attachment is { filename: string; base64: string } => attachment !== null);
  if (attachments.length === 0) {
    return NextResponse.json({ error: "At least one photo is required." }, { status: 400 });
  }

  const apiKey = process.env.RESEND_API_KEY;
  if (!apiKey) {
    console.error("service-requests: RESEND_API_KEY is not set");
    return NextResponse.json({ error: "The request could not be sent right now. Please email us directly." }, { status: 503 });
  }

  const kindLabel = kind === "repair" ? "Repair request" : "Consignment submission";
  const rows = [
    ...itemEntries.map(([key, value]) => [labelize(key), value] as const),
    ["Name", name],
    ["Email", email],
    ...(phone ? [["Phone", phone] as const] : []),
    ["Photos", String(attachments.length)],
  ];
  const html = `
    <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;color:#14110f">
      <h2 style="margin:0 0 4px">${kindLabel}</h2>
      <p style="margin:0 0 16px;color:#6b6b6b">Submitted from overhaultrading.com</p>
      <table style="border-collapse:collapse;font-size:14px">
        ${rows
          .map(
            ([key, value]) =>
              `<tr><td style="padding:6px 16px 6px 0;vertical-align:top;color:#8a7a5c;white-space:nowrap">${escapeHtml(
                key,
              )}</td><td style="padding:6px 0;vertical-align:top">${escapeHtml(value).replace(/\n/g, "<br>")}</td></tr>`,
          )
          .join("")}
      </table>
      <p style="margin:16px 0 0;font-size:13px;color:#8a7a5c">Reply to this email to reach ${escapeHtml(name)}.</p>
    </div>`;

  try {
    const response = await fetch("https://api.resend.com/emails", {
      method: "POST",
      headers: { Authorization: `Bearer ${apiKey}`, "Content-Type": "application/json" },
      body: JSON.stringify({
        from: FROM,
        to: [TO],
        reply_to: email,
        subject: `${kindLabel} — ${itemEntries[0]?.[1]?.slice(0, 60) ?? name}`,
        html,
        attachments: attachments.map((attachment) => ({ filename: attachment.filename, content: attachment.base64 })),
      }),
    });

    if (!response.ok) {
      const detail = await response.text();
      console.error("service-requests: resend send failed", response.status, detail);
      return NextResponse.json({ error: "The request could not be sent right now. Please email us directly." }, { status: 502 });
    }
  } catch (error) {
    console.error("service-requests: resend send threw", error);
    return NextResponse.json({ error: "The request could not be sent right now. Please email us directly." }, { status: 502 });
  }

  return NextResponse.json({ ok: true });
}

function labelize(key: string): string {
  return key
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/^./, (character) => character.toUpperCase());
}
