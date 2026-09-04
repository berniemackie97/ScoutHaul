import { type NextRequest, NextResponse } from "next/server";

/**
 * First-dibs signups. Emails the address to the shop inbox via Resend, the same way the consignment
 * and repair intake at /api/service-requests does — same key, same sender, no SDK.
 *
 * This exists because the form used to do nothing at all: its submit handler set a "You're on the
 * list" message and made no request, so every signup was a false confirmation and every lead was
 * lost. Public anonymous traffic, so everything is validated and bounded here.
 */

const TO = process.env.RESEND_TO ?? "overhaultradingco@gmail.com";
// Until a domain is verified in Resend, sends must come from the shared onboarding sender.
const FROM = process.env.RESEND_FROM ?? "Overhaul Trading <onboarding@resend.dev>";

const MAX_EMAIL_LEN = 254; // RFC 5321
const MAX_SOURCE_LEN = 60;

type IncomingBody = {
  email?: string;
  /** Where on the site they signed up, for context in the notification. */
  source?: string;
  /** Honeypot — real users never see or fill this. */
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

export async function POST(request: NextRequest) {
  let body: IncomingBody;
  try {
    body = (await request.json()) as IncomingBody;
  } catch {
    return NextResponse.json({ error: "Invalid request." }, { status: 400 });
  }

  // Honeypot: answer as if it worked, send nothing. A bot learns nothing from the difference.
  if (body.company) return NextResponse.json({ ok: true });

  const email = (body.email ?? "").trim();
  if (!email || email.length > MAX_EMAIL_LEN || !isEmail(email)) {
    return NextResponse.json({ error: "Enter a valid email address." }, { status: 400 });
  }

  const source = (body.source ?? "").trim().slice(0, MAX_SOURCE_LEN) || "storefront";

  const apiKey = process.env.RESEND_API_KEY;
  if (!apiKey) {
    console.error("newsletter: RESEND_API_KEY is not set");
    return NextResponse.json(
      { error: "We couldn't sign you up right now. Please email us directly." },
      { status: 503 },
    );
  }

  const html = `
    <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;color:#14110f">
      <h2 style="margin:0 0 4px">First dibs signup</h2>
      <p style="margin:0 0 16px;color:#6b6b6b">Submitted from overhaultrading.com (${escapeHtml(source)})</p>
      <p style="margin:0;font-size:16px"><strong>${escapeHtml(email)}</strong></p>
      <p style="margin:16px 0 0;font-size:13px;color:#8a7a5c">Reply to this email to reach them.</p>
    </div>`;

  try {
    const response = await fetch("https://api.resend.com/emails", {
      method: "POST",
      headers: { Authorization: `Bearer ${apiKey}`, "Content-Type": "application/json" },
      body: JSON.stringify({
        from: FROM,
        to: [TO],
        reply_to: email,
        subject: `First dibs signup — ${email}`,
        html,
      }),
    });

    if (!response.ok) {
      const detail = await response.text();
      console.error("newsletter: resend send failed", response.status, detail);
      return NextResponse.json(
        { error: "We couldn't sign you up right now. Please email us directly." },
        { status: 502 },
      );
    }
  } catch (error) {
    console.error("newsletter: resend send threw", error);
    return NextResponse.json(
      { error: "We couldn't sign you up right now. Please email us directly." },
      { status: 502 },
    );
  }

  return NextResponse.json({ ok: true });
}
