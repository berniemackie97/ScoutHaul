import Link from "next/link";
import { ArrowRight, BadgeCheck } from "lucide-react";
import { Reveal } from "@/components/ui/reveal";

export const metadata = {
  title: "How we grade",
  description:
    "Every item is inspected, tested, and graded against an honest scale before it hits the floor. Here's exactly how — no inflation, no surprises.",
};

const grades = [
  { tier: "Excellent", dots: 5, blurb: "Like new. Minimal signs of use, fully functional, ready to go." },
  { tier: "Very good", dots: 4, blurb: "Light, honest wear. Works exactly as intended — nothing that affects use." },
  { tier: "Good", dots: 3, blurb: "Wear consistent with age and use. Solid and functional, flaws noted in photos." },
  { tier: "Fair", dots: 2, blurb: "Heavier wear or cosmetic flaws, all documented. Still does its job." },
  { tier: "Project / parts", dots: 1, blurb: "Sold as-is for restoration or parts. Every flaw spelled out up front." },
];

const steps = [
  { no: "01", title: "Sourced", body: "Estate sales, liquidations, and smart buys — chosen for quality, not volume." },
  { no: "02", title: "Inspected", body: "Every item examined by hand. Defects found, noted, and photographed." },
  { no: "03", title: "Tested", body: "Electronics powered on and run through their functions. Mechanicals worked." },
  { no: "04", title: "Graded", body: "Assigned an honest grade against the scale — no inflation, ever." },
  { no: "05", title: "Documented", body: "Photographed as-is and described straight. The wear is in the listing." },
  { no: "06", title: "Listed", body: "On the floor, certified honest. What you see is what shows up." },
];

export default function GradingPage() {
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
              The promise
            </p>
            <h1 className="mt-7 font-heading text-[clamp(2.75rem,7vw,5.5rem)] font-semibold leading-[0.92] tracking-[-0.025em]">
              How we grade.
            </h1>
            <p className="mt-7 max-w-xl text-lg leading-relaxed text-background/70">
              Every piece earns its grade by hand. We&rsquo;d rather undersell the condition than oversell it —
              because &ldquo;no surprises&rdquo; only means something if the grading is honest.
            </p>
          </Reveal>
        </div>
      </section>

      {/* Grade scale */}
      <section className="mx-auto max-w-5xl px-6 py-24">
        <Reveal>
          <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">The scale</p>
          <h2 className="mt-3 font-heading text-4xl font-semibold tracking-[-0.02em] text-ink">Five honest grades</h2>
        </Reveal>
        <div className="mt-12">
          {grades.map((g, i) => (
            <Reveal key={g.tier} delay={i * 70}>
              <div className="grid items-center gap-x-6 gap-y-2 border-t border-rule py-6 sm:grid-cols-[12rem_1fr]">
                <div className="flex items-center gap-3">
                  <div className="flex gap-1" aria-hidden>
                    {Array.from({ length: 5 }).map((_, d) => (
                      <span key={d} className={`h-2 w-2 rounded-full ${d < g.dots ? "bg-copper" : "bg-rule"}`} />
                    ))}
                  </div>
                  <span className="font-heading text-xl font-medium text-ink">{g.tier}</span>
                </div>
                <p className="text-sm leading-relaxed text-slate">{g.blurb}</p>
              </div>
            </Reveal>
          ))}
          <Reveal delay={400}>
            <p className="mt-6 border-l-2 border-copper bg-copper/[0.05] px-5 py-4 text-sm leading-relaxed text-slate">
              <span className="font-mono text-[10px] uppercase tracking-[0.18em] text-copper">Serviced</span> — when
              we&rsquo;ve repaired or refurbished an item to working order, we say so and list exactly what was done.
            </p>
          </Reveal>
        </div>
      </section>

      {/* Process — dark band */}
      <section className="bg-ink text-background">
        <div className="mx-auto max-w-7xl px-6 py-24">
          <Reveal>
            <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">The process</p>
            <h2 className="mt-3 font-heading text-4xl font-semibold tracking-[-0.02em] text-background">
              From the field to the floor
            </h2>
          </Reveal>
          <div className="mt-12 grid gap-x-10 gap-y-10 sm:grid-cols-2 lg:grid-cols-3">
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

      {/* Guarantee + CTA */}
      <section className="mx-auto max-w-3xl px-6 py-24 text-center">
        <Reveal>
          <span className="inline-flex h-16 w-16 items-center justify-center rounded-full border-2 border-copper text-copper">
            <BadgeCheck className="h-8 w-8" aria-hidden />
          </span>
          <h2 className="mt-7 font-heading text-[clamp(2rem,4vw,3rem)] font-semibold tracking-[-0.02em] text-ink">
            If it&rsquo;s not as described, send it back.
          </h2>
          <p className="mx-auto mt-4 max-w-md leading-relaxed text-slate">
            Every order is backed by easy returns. The grade is a promise, not a sales pitch.
          </p>
          <Link
            href="/products"
            className="group mt-9 inline-flex items-center gap-2.5 bg-ink px-7 py-3.5 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:bg-copper"
          >
            Shop the floor
            <ArrowRight className="h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" aria-hidden />
          </Link>
        </Reveal>
      </section>
    </div>
  );
}
