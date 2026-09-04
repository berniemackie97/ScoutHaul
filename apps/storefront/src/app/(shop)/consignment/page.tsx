import { Reveal } from "@/components/ui/reveal";
import { ServiceRequestForm } from "@/components/store/service-request-form";

export const metadata = {
  title: "Consignment",
  description:
    "Consign quality furniture, electronics, and collectibles with Overhaul Trading Company. We photograph, grade, list, and ship — you get paid when it sells, on clear terms.",
  alternates: { canonical: "/consignment" },
};

const benefits = [
  { no: "01", title: "A fair split", body: "70% to you, 30% to us. Simple and transparent — you're paid as soon as it sells." },
  { no: "02", title: "We handle everything", body: "Photography, honest grading, listing, buyer questions, and shipping. You hand it over and we take it from there." },
  { no: "03", title: "Real reach", body: "Listed on our floor and the channels that move quality goods fast, priced to actually sell." },
];

export default function ConsignmentPage() {
  return (
    <div>
      {/* Hero — dark */}
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
              Have something good?
              <br />
              <span className="text-copper">Let us sell it.</span>
            </h1>
            <p className="mt-7 max-w-xl text-lg leading-relaxed text-background/70">
              Quality furniture, electronics, or collectibles you&rsquo;re ready to part with — hand it to us. We
              photograph, grade, list, and ship it, and you get paid when it sells. Clear terms, no hassle.
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
        <div className="mx-auto max-w-2xl px-6 py-20">
          <Reveal>
            <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">Submit an item</p>
            <h2 className="mt-3 font-heading text-3xl font-semibold tracking-[-0.02em] text-ink">Start your consignment</h2>
            <p className="mt-3 text-sm leading-relaxed text-slate">
              A few details and a couple of photos is all we need. We&rsquo;ll tell you if it&rsquo;s a fit and what we
              expect it to fetch — no pressure.
            </p>
            <div className="mt-10">
              <ServiceRequestForm kind="consignment" />
            </div>
          </Reveal>
        </div>
      </section>
    </div>
  );
}
