"use client";

import { useState, type FormEvent } from "react";
import { BadgeCheck, HandCoins } from "lucide-react";
import { Reveal } from "@/components/ui/reveal";

const benefits = [
  { no: "01", title: "Fair split", body: "70% to you, 30% to us. Simple, transparent, paid when it sells." },
  { no: "02", title: "We do the work", body: "Photography, honest grading, listing, customer service, and shipping." },
  { no: "03", title: "Real reach", body: "Listed on our floor and the channels that move quality goods fast." },
];

const inputClass =
  "w-full border border-rule bg-background px-4 py-3 text-sm text-ink outline-none transition-colors placeholder:text-slate/50 focus:border-copper";
const labelClass = "mb-2 block font-mono text-[11px] uppercase tracking-[0.16em] text-slate/70";

export default function ConsignmentPage() {
  const [submitted, setSubmitted] = useState(false);

  if (submitted) {
    return (
      <div className="mx-auto max-w-md px-6 py-32 text-center">
        <span className="inline-flex h-16 w-16 items-center justify-center rounded-full border-2 border-copper text-copper">
          <BadgeCheck className="h-8 w-8" aria-hidden />
        </span>
        <h1 className="mt-7 font-heading text-3xl font-semibold tracking-[-0.02em] text-ink">Submission received.</h1>
        <p className="mt-3 leading-relaxed text-slate">
          We&rsquo;ll review your item and get back to you within 48 hours with next steps.
        </p>
        <button
          onClick={() => setSubmitted(false)}
          className="mt-8 border-b border-ink/30 pb-1 font-mono text-[11px] uppercase tracking-[0.16em] text-ink transition-colors hover:border-copper hover:text-copper"
        >
          Submit another
        </button>
      </div>
    );
  }

  return (
    <div>
      {/* Header — dark */}
      <section className="relative overflow-hidden bg-ink text-background">
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0"
          style={{ backgroundImage: "radial-gradient(55% 60% at 85% -10%, hsl(30 64% 52% / 0.20), transparent 62%)" }}
        />
        <div className="relative mx-auto max-w-7xl px-6 py-24 md:py-28">
          <Reveal>
            <p className="flex items-center gap-3 font-mono text-[11px] uppercase tracking-[0.3em] text-copper">
              <span aria-hidden className="inline-block h-px w-10 bg-copper" />
              Consignment
            </p>
            <h1 className="mt-7 font-heading text-[clamp(2.75rem,7vw,5.5rem)] font-semibold leading-[0.92] tracking-[-0.025em]">
              Sell your stuff.
              <br />
              <span className="text-copper">We do the work.</span>
            </h1>
            <p className="mt-7 max-w-xl text-lg leading-relaxed text-background/70">
              Have quality goods you&rsquo;re done with? Hand them over. We photograph, grade, list, and ship —
              you get paid when it sells, on clear terms.
            </p>
          </Reveal>
        </div>
      </section>

      {/* Benefits */}
      <section className="mx-auto max-w-7xl px-6 py-20">
        <div className="grid gap-x-10 gap-y-10 md:grid-cols-3">
          {benefits.map((b, i) => (
            <Reveal key={b.no} delay={i * 80}>
              <div className="border-t border-copper/50 pt-5">
                <div className="flex items-baseline gap-3">
                  <span className="font-mono text-xs text-copper">{b.no}</span>
                  <h3 className="font-heading text-lg font-medium text-ink">{b.title}</h3>
                </div>
                <p className="mt-2.5 text-sm leading-relaxed text-slate">{b.body}</p>
              </div>
            </Reveal>
          ))}
        </div>
      </section>

      {/* Form */}
      <section className="border-t border-rule bg-surface/50">
        <div className="mx-auto max-w-2xl px-6 py-24">
          <Reveal>
            <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">Submit an item</p>
            <h2 className="mt-3 font-heading text-3xl font-semibold tracking-[-0.02em] text-ink">
              Tell us what you&rsquo;ve got
            </h2>
            <p className="mt-3 text-sm leading-relaxed text-slate">
              We&rsquo;ll let you know if it&rsquo;s a good fit and what we expect it to fetch — no pressure.
            </p>

            <form onSubmit={(e: FormEvent) => { e.preventDefault(); setSubmitted(true); }} className="mt-8 space-y-5">
              <div>
                <label htmlFor="item" className={labelClass}>What are you selling?</label>
                <input id="item" required placeholder="e.g. Eames-style lounge chair & ottoman" className={inputClass} />
              </div>
              <div className="grid gap-5 sm:grid-cols-2">
                <div>
                  <label htmlFor="category" className={labelClass}>Category</label>
                  <select id="category" required defaultValue="" className={inputClass}>
                    <option value="" disabled>Select…</option>
                    <option>Furniture</option>
                    <option>Electronics</option>
                    <option>Collectibles</option>
                    <option>Household</option>
                    <option>Other</option>
                  </select>
                </div>
                <div>
                  <label htmlFor="condition" className={labelClass}>Condition</label>
                  <select id="condition" required defaultValue="" className={inputClass}>
                    <option value="" disabled>Select…</option>
                    <option>Excellent</option>
                    <option>Very good</option>
                    <option>Good</option>
                    <option>Fair</option>
                    <option>Project / parts</option>
                  </select>
                </div>
              </div>
              <div>
                <label htmlFor="value" className={labelClass}>What do you think it&rsquo;s worth? ($)</label>
                <input id="value" type="number" min="0" placeholder="Estimated value" className={inputClass} />
              </div>
              <div>
                <label htmlFor="notes" className={labelClass}>Anything we should know?</label>
                <textarea id="notes" rows={3} placeholder="History, flaws, accessories, original receipts…" className={inputClass} />
              </div>
              <div className="grid gap-5 sm:grid-cols-2">
                <div>
                  <label htmlFor="name" className={labelClass}>Your name</label>
                  <input id="name" required placeholder="Full name" className={inputClass} />
                </div>
                <div>
                  <label htmlFor="email" className={labelClass}>Email</label>
                  <input id="email" type="email" required placeholder="you@email.com" className={inputClass} />
                </div>
              </div>
              <button
                type="submit"
                className="group inline-flex w-full items-center justify-center gap-2.5 bg-ink px-6 py-3.5 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:bg-copper"
              >
                <HandCoins className="h-4 w-4" aria-hidden />
                Submit for review
              </button>
            </form>
          </Reveal>
        </div>
      </section>
    </div>
  );
}
