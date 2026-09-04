import Link from "next/link";
import { ArrowRight } from "lucide-react";
import { Reveal } from "@/components/ui/reveal";

export const metadata = {
  title: "About",
  description:
    "Overhaul Trading Company — a Bradenton, Florida goods house. Quality pre-owned furniture, electronics, and collectibles, each one inspected, graded honestly, and backed by us.",
  alternates: { canonical: "/about" },
};

const values = [
  { no: "01", title: "Sourced with an eye", body: "Estate sales, liquidations, and private finds. We buy for quality and character — never to fill a shelf." },
  { no: "02", title: "Graded honestly", body: "Every item inspected, tested, and graded on one plain, consistent scale. We would rather undersell than overpromise." },
  { no: "03", title: "Priced fairly", body: "Real market value, usually well below retail. A genuine deal for you, and sustainable for us." },
  { no: "04", title: "Kept in use", body: "Every piece we move is one kept out of a landfill. Better for your wallet, and for the planet." },
];

export default function AboutPage() {
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
              Our story
            </p>
            <h1 className="mt-7 max-w-3xl font-heading text-[clamp(2.5rem,6.5vw,5rem)] font-semibold leading-[0.94] tracking-[-0.025em]">
              Pre-owned, held to a higher standard.
            </h1>
            <p className="mt-7 max-w-xl text-lg leading-relaxed text-background/70">
              Overhaul Trading Company finds quality furniture, electronics, and collectibles, restores and inspects
              every piece by hand, and grades it honestly — then stands behind all of it. Based in Bradenton,
              Florida; shipping nationwide.
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
              A remarkable amount of genuinely good furniture, electronics, and gear is thrown out every year — not
              because it stopped working, but because selling it properly takes work: testing, cleaning, honest
              grading, careful packing. That work is the whole company.
            </p>
            <p className="leading-relaxed">
              Every piece is inspected and tested before it is listed. The condition is documented plainly, the price
              reflects it, and if anything arrives different from the listing, it comes straight back.{" "}
              <span className="text-ink">Certified honest — no surprises.</span>
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

      {/* Closing CTA — single band, location folded in (footer carries the full contact details) */}
      <section className="mx-auto max-w-7xl px-6 pb-28 pt-4 text-center">
        <Reveal>
          <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">
            Bradenton, FL · Shipping nationwide · Local pickup on the Gulf Coast
          </p>
          <h2 className="mx-auto mt-4 max-w-2xl font-heading text-[clamp(1.75rem,3.5vw,2.75rem)] font-semibold tracking-[-0.02em] text-ink">
            Come find something good.
          </h2>
          <Link
            href="/products"
            className="group mt-8 inline-flex items-center gap-2.5 bg-ink px-7 py-3.5 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:bg-copper"
          >
            Shop the floor
            <ArrowRight className="h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" aria-hidden />
          </Link>
        </Reveal>
      </section>
    </div>
  );
}
