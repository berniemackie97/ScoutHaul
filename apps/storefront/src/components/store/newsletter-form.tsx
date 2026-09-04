"use client";

import { useState, type FormEvent } from "react";
import { ArrowRight, Check } from "lucide-react";

/**
 * First-dibs signup.
 *
 * This used to be a form whose submit handler was `// TODO: wire to the list provider once chosen`
 * followed by `setDone(true)`. It made no request. Every shopper was told "You're on the list", and
 * every one of those leads was lost — the confirmation cost the sale and the trust at once.
 *
 * It now posts to /api/newsletter, which mails the address to the shop inbox through Resend, the same
 * path the consignment and repair intake already uses. Success is shown only after the server says so.
 */
export function NewsletterForm() {
  const [email, setEmail] = useState("");
  const [company, setCompany] = useState("");
  const [status, setStatus] = useState<"idle" | "sending" | "done">("idle");
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!email.trim() || status === "sending") return;

    setStatus("sending");
    setError(null);
    try {
      const response = await fetch("/api/newsletter", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: email.trim(), source: "home", company }),
      });
      if (!response.ok) {
        const detail = (await response.json().catch(() => null)) as { error?: string } | null;
        setError(detail?.error ?? "We couldn't sign you up right now. Please try again.");
        setStatus("idle");
        return;
      }
      setStatus("done");
    } catch {
      // The request never left the browser — offline, or blocked.
      setError("We couldn't reach the shop. Check your connection and try again.");
      setStatus("idle");
    }
  }

  if (status === "done") {
    return (
      <p className="mt-8 inline-flex items-center gap-2 font-mono text-[12px] uppercase tracking-[0.16em] text-copper">
        <Check className="h-4 w-4" aria-hidden />
        You&rsquo;re on the list — watch your inbox.
      </p>
    );
  }

  return (
    <form onSubmit={onSubmit} className="mx-auto mt-8 flex max-w-md flex-col gap-3">
      <div className="flex flex-col gap-3 sm:flex-row">
        <label htmlFor="newsletter-email" className="sr-only">
          Email address
        </label>
        <input
          id="newsletter-email"
          type="email"
          required
          autoComplete="email"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          placeholder="you@email.com"
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? "newsletter-error" : undefined}
          className="flex-1 border border-rule bg-background px-4 py-3 text-sm text-ink outline-none transition-colors placeholder:text-slate/55 focus:border-copper"
        />
        {/* Honeypot: off-screen and skipped by the keyboard, so only a bot fills it. */}
        <input
          type="text"
          name="company"
          tabIndex={-1}
          autoComplete="off"
          aria-hidden="true"
          value={company}
          onChange={(event) => setCompany(event.target.value)}
          className="absolute left-[-9999px] h-0 w-0 opacity-0"
        />
        <button
          type="submit"
          disabled={status === "sending"}
          className="group inline-flex items-center justify-center gap-2 bg-ink px-6 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:bg-copper disabled:opacity-60"
        >
          {status === "sending" ? "Signing up…" : "Get first dibs"}
          <ArrowRight className="h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" aria-hidden />
        </button>
      </div>
      {error ? (
        <p id="newsletter-error" role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
    </form>
  );
}
