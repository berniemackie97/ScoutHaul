import { Reveal } from "@/components/ui/reveal";
import { ServiceRequestForm } from "@/components/store/service-request-form";

export const metadata = {
  title: "Repairs",
  description:
    "Honest repair for electronics, furniture, and appliances at Overhaul Trading Company. Free no-obligation quotes, real workmanship, and a straight answer about whether it's worth fixing.",
  alternates: { canonical: "/repairs" },
};

const repairTypes = [
  { title: "Electronics", body: "Laptops, phones, tablets, audio, gaming." },
  { title: "Furniture", body: "Chairs, tables, dressers, shelving, upholstery." },
  { title: "Appliances", body: "Small appliances and kitchen equipment." },
  { title: "General", body: "Tools, equipment, and the odd one-off." },
];

const steps = [
  { no: "01", title: "Submit", body: "Tell us what needs fixing and send a few photos of the problem." },
  { no: "02", title: "Quote", body: "We diagnose it and send a fair, no-obligation price." },
  { no: "03", title: "Repair", body: "Once you approve, we get to work — quality over speed." },
  { no: "04", title: "Pick up", body: "Grab it locally or we ship it back. Good as it gets." },
];

export default function RepairsPage() {
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
              The workshop
            </p>
            <h1 className="mt-7 font-heading text-[clamp(2.75rem,7vw,5.5rem)] font-semibold leading-[0.92] tracking-[-0.025em]">
              Don&rsquo;t replace it.
              <br />
              <span className="text-copper">Repair it.</span>
            </h1>
            <p className="mt-7 max-w-xl text-lg leading-relaxed text-background/70">
              Honest repair for electronics, furniture, and appliances. Fair pricing, real workmanship, and a straight
              answer about whether it&rsquo;s worth fixing.
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
          <div className="mt-10">
            <ServiceRequestForm kind="repair" />
          </div>
        </Reveal>
      </section>
    </div>
  );
}
