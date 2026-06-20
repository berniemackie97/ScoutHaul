"use client";

import { useState, type FormEvent } from "react";
import { ArrowRight, Check } from "lucide-react";

export function NewsletterForm() {
  const [email, setEmail] = useState("");
  const [done, setDone] = useState(false);

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (!email.trim()) return;
    // TODO: wire to the list provider once chosen. Optimistic confirm for now.
    setDone(true);
  }

  if (done) {
    return (
      <p className="mt-8 inline-flex items-center gap-2 font-mono text-[12px] uppercase tracking-[0.16em] text-copper">
        <Check className="h-4 w-4" aria-hidden />
        You&rsquo;re on the list — watch your inbox.
      </p>
    );
  }

  return (
    <form onSubmit={onSubmit} className="mx-auto mt-8 flex max-w-md flex-col gap-3 sm:flex-row">
      <label htmlFor="newsletter-email" className="sr-only">
        Email address
      </label>
      <input
        id="newsletter-email"
        type="email"
        required
        value={email}
        onChange={(e) => setEmail(e.target.value)}
        placeholder="you@email.com"
        className="flex-1 border border-rule bg-background px-4 py-3 text-sm text-ink outline-none transition-colors placeholder:text-slate/55 focus:border-copper"
      />
      <button
        type="submit"
        className="group inline-flex items-center justify-center gap-2 bg-ink px-6 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:bg-copper"
      >
        Get first dibs
        <ArrowRight className="h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" aria-hidden />
      </button>
    </form>
  );
}
