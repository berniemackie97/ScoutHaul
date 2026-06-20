"use client";

import { useState, type FormEvent } from "react";
import { BadgeCheck, Wrench } from "lucide-react";
import { Reveal } from "@/components/ui/reveal";

const repairTypes = [
  { title: "Electronics", body: "Laptops, phones, tablets, audio, gaming." },
  { title: "Furniture", body: "Chairs, tables, dressers, shelving, upholstery." },
  { title: "Appliances", body: "Small appliances and kitchen equipment." },
  { title: "General", body: "Tools, equipment, and the odd one-off." },
];

const steps = [
  { no: "01", title: "Submit", body: "Tell us what needs fixing and what's wrong with it." },
  { no: "02", title: "Quote", body: "We diagnose it and send a fair, no-obligation price." },
  { no: "03", title: "Repair", body: "Once you approve, we get to work — quality over speed." },
  { no: "04", title: "Pick up", body: "Grab it locally or we ship it back. Good as it gets." },
];

const inputClass =
  "w-full border border-rule bg-background px-4 py-3 text-sm text-ink outline-none transition-colors placeholder:text-slate/50 focus:border-copper";
const labelClass = "mb-2 block font-mono text-[11px] uppercase tracking-[0.16em] text-slate/70";

export default function RepairsPage() {
  const [submitted, setSubmitted] = useState(false);

  if (submitted) {
    return (
      <div className="mx-auto max-w-md px-6 py-32 text-center">
        <span className="inline-flex h-16 w-16 items-center justify-center rounded-full border-2 border-copper text-copper">
          <BadgeCheck className="h-8 w-8" aria-hidden />
        </span>
        <h1 className="mt-7 font-heading text-3xl font-semibold tracking-[-0.02em] text-ink">Request received.</h1>
        <p className="mt-3 leading-relaxed text-slate">
          We&rsquo;ll review it and send a quote within 24–48 hours. No obligation, no surprises.
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
              The workshop
            </p>
            <h1 className="mt-7 font-heading text-[clamp(2.75rem,7vw,5.5rem)] font-semibold leading-[0.92] tracking-[-0.025em]">
              Don&rsquo;t replace it.
              <br />
              <span className="text-copper">Repair it.</span>
            </h1>
            <p className="mt-7 max-w-xl text-lg leading-relaxed text-background/70">
              Honest repair for electronics, furniture, and appliances. Fair pricing, real workmanship, and a
              straight answer about whether it&rsquo;s worth fixing.
            </p>
          </Reveal>
        </div>
      </section>

      {/* What we repair */}
      <section className="mx-auto max-w-7xl px-6 py-20">
        <div className="grid gap-x-10 gap-y-10 sm:grid-cols-2 lg:grid-cols-4">
          {repairTypes.map((t, i) => (
            <Reveal key={t.title} delay={i * 70}>
              <div className="border-t border-copper/50 pt-5">
                <h3 className="font-heading text-lg font-medium text-ink">{t.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-slate">{t.body}</p>
              </div>
            </Reveal>
          ))}
        </div>
      </section>

      {/* Process — dark band */}
      <section className="bg-ink text-background">
        <div className="mx-auto max-w-7xl px-6 py-20">
          <Reveal>
            <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">How it works</p>
            <h2 className="mt-3 font-heading text-4xl font-semibold tracking-[-0.02em] text-background">
              Four steps, no runaround
            </h2>
          </Reveal>
          <div className="mt-12 grid gap-x-10 gap-y-10 sm:grid-cols-2 lg:grid-cols-4">
            {steps.map((s, i) => (
              <Reveal key={s.no} delay={i * 70}>
                <div className="border-t border-copper/40 pt-5">
                  <span className="font-mono text-xs text-copper">{s.no}</span>
                  <h3 className="mt-2 font-heading text-xl font-medium text-background">{s.title}</h3>
                  <p className="mt-2 text-sm leading-relaxed text-background/60">{s.body}</p>
                </div>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      {/* Request form */}
      <section className="mx-auto max-w-2xl px-6 py-24">
        <Reveal>
          <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">Get a quote</p>
          <h2 className="mt-3 font-heading text-3xl font-semibold tracking-[-0.02em] text-ink">Request a repair</h2>
          <p className="mt-3 text-sm leading-relaxed text-slate">
            Free quotes, no obligation. We only proceed with your approval — most repairs done in 3–5 business days.
          </p>

          <form onSubmit={(e: FormEvent) => { e.preventDefault(); setSubmitted(true); }} className="mt-8 space-y-5">
            <div className="grid gap-5 sm:grid-cols-2">
              <div>
                <label htmlFor="item" className={labelClass}>Item</label>
                <input id="item" required placeholder="e.g. Marantz 2230 receiver" className={inputClass} />
              </div>
              <div>
                <label htmlFor="category" className={labelClass}>Category</label>
                <select id="category" required defaultValue="" className={inputClass}>
                  <option value="" disabled>Select…</option>
                  <option>Electronics</option>
                  <option>Furniture</option>
                  <option>Appliances</option>
                  <option>General</option>
                </select>
              </div>
            </div>
            <div>
              <label htmlFor="issue" className={labelClass}>What&rsquo;s wrong with it?</label>
              <textarea id="issue" required rows={4} placeholder="Describe the issue in as much detail as you can…" className={inputClass} />
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
              <Wrench className="h-4 w-4" aria-hidden />
              Submit repair request
            </button>
          </form>
        </Reveal>
      </section>
    </div>
  );
}
