import Link from "next/link";
import { ArrowRight, BadgeCheck, MapPin, Mail } from "lucide-react";
import { Reveal } from "@/components/ui/reveal";

export const metadata = {
  title: "About",
  description:
    "Overhaul Trading Company — a Bradenton, Florida resale operation. Quality secondhand goods, sourced carefully, graded honestly, delivered reliably.",
};

const values = [
  { no: "01", title: "Sourced with intent", body: "Estate sales, liquidations, and smart buys — chosen for quality and character, never volume." },
  { no: "02", title: "Graded honestly", body: "Every item inspected, tested, and graded against a plain scale. We'd rather undersell than oversell." },
  { no: "03", title: "Priced fairly", body: "Typically 50–60% below retail. Good value for the buyer, sustainable for the business." },
  { no: "04", title: "Kept in use", body: "Every piece we move is one less in a landfill. A second life, done right." },
];

const contact = [
  { icon: MapPin, label: "1675 55th Avenue Cir E, Suite 201 · Bradenton, FL 34203" },
  { icon: Mail, label: "hello@overhaultrading.com" },
];

export default function AboutPage() {
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
              Our story
            </p>
            <h1 className="mt-7 max-w-3xl font-heading text-[clamp(2.5rem,6.5vw,5rem)] font-semibold leading-[0.94] tracking-[-0.025em]">
              A serious operator, not a flea-market stall.
            </h1>
            <p className="mt-7 max-w-xl text-lg leading-relaxed text-background/70">
              Overhaul Trading Company is a Bradenton, Florida resale business. We source quality secondhand
              goods strategically, grade them honestly, and stand behind everything we sell.
            </p>
          </Reveal>
        </div>
      </section>

      {/* Story + values */}
      <section className="mx-auto max-w-7xl px-6 py-24">
        <div className="grid gap-12 lg:grid-cols-[1fr_1fr] lg:gap-20">
          <Reveal>
            <h2 className="font-heading text-[clamp(2rem,4vw,3rem)] font-semibold leading-[1.05] tracking-[-0.02em] text-ink">
              Quality goods deserve a second life.
            </h2>
          </Reveal>
          <Reveal delay={120} className="space-y-5 text-slate">
            <p className="leading-relaxed">
              Too much good furniture, electronics, and equipment ends up discarded — not because it&rsquo;s broken,
              but because reselling it honestly takes work most people won&rsquo;t do. We do that work.
            </p>
            <p className="leading-relaxed">
              Every item is inspected, tested, and graded before it reaches the floor. The wear is in the listing,
              the price is fair, and if something arrives not as described, it comes back. That&rsquo;s the whole
              business: <span className="text-ink">good finds, no surprises.</span>
            </p>
          </Reveal>
        </div>

        <div className="mt-16 grid gap-x-10 gap-y-10 sm:grid-cols-2 lg:grid-cols-4">
          {values.map((v, i) => (
            <Reveal key={v.no} delay={i * 80}>
              <div className="border-t border-copper/50 pt-5">
                <div className="flex items-baseline gap-3">
                  <span className="font-mono text-xs text-copper">{v.no}</span>
                  <h3 className="font-heading text-lg font-medium text-ink">{v.title}</h3>
                </div>
                <p className="mt-2.5 text-sm leading-relaxed text-slate">{v.body}</p>
              </div>
            </Reveal>
          ))}
        </div>
      </section>

      {/* Location / contact — dark band */}
      <section className="bg-ink text-background">
        <div className="mx-auto max-w-7xl px-6 py-20">
          <div className="grid gap-12 lg:grid-cols-[1fr_1fr]">
            <Reveal>
              <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">Find us</p>
              <h2 className="mt-3 font-heading text-3xl font-semibold tracking-[-0.02em] text-background">
                Bradenton, Florida
              </h2>
              <p className="mt-4 max-w-md leading-relaxed text-background/65">
                Shop online and we&rsquo;ll ship nationwide, or arrange local pickup right here on the Gulf Coast.
              </p>
              <p className="mt-6 inline-flex items-center gap-2 font-mono text-[10px] uppercase tracking-[0.18em] text-copper">
                <BadgeCheck className="h-4 w-4" aria-hidden /> Every item inspected &amp; graded
              </p>
            </Reveal>
            <Reveal delay={120}>
              <ul className="space-y-4">
                {contact.map((c) => (
                  <li key={c.label} className="flex items-center gap-3 text-sm text-background/75">
                    <c.icon className="h-4 w-4 shrink-0 text-copper" aria-hidden />
                    {c.label}
                  </li>
                ))}
              </ul>
            </Reveal>
          </div>
        </div>
      </section>

      {/* CTA */}
      <section className="mx-auto max-w-7xl px-6 py-20 text-center">
        <Reveal>
          <Link
            href="/products"
            className="group inline-flex items-center gap-2.5 bg-ink px-7 py-3.5 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:bg-copper"
          >
            Shop the floor
            <ArrowRight className="h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" aria-hidden />
          </Link>
        </Reveal>
      </section>
    </div>
  );
}
