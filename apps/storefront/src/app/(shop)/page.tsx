import Link from "next/link";
import Image from "next/image";
import { ArrowRight, ArrowUpRight, BadgeCheck } from "lucide-react";
import { ProductCard } from "@/components/store/product-card";
import { NewsletterForm } from "@/components/store/newsletter-form";
import { Reveal } from "@/components/ui/reveal";
import { getListings, getCategories } from "@/lib/catalog";

const principles = [
  { title: "Inspected & honest", body: "Every item tested and described as-is. The condition is in the listing — never buried." },
  { title: "Sourced & priced fair", body: "Estate sales, liquidation, smart buys. Typically 50–60% below retail." },
  { title: "Ships fast · local pickup", body: "Quick dispatch nationwide. Pickup available here in Bradenton." },
  { title: "A second life", body: "Quality goods kept in use — better for your wallet, and for the planet." },
];

const services = [
  { no: "01", title: "Buy with confidence", body: "Every item is inspected and graded straight. If it isn't as described, send it back.", href: "/grading" },
  { no: "02", title: "Repairs", body: "Electronics, furniture, and appliances brought back to working order at a fair rate.", href: "/repairs" },
  { no: "03", title: "Consignment", body: "Have something good you're done with? We'll sell it for you. Clear terms, fair split.", href: "/consignment" },
];

const heroStats: [string, string][] = [
  ["247", "items live"],
  ["100%", "inspected"],
  ["50–60%", "below retail"],
  ["FL", "ships from"],
];

const ticker = ["Inspected", "Graded straight", "Certified honest", "No surprises", "Sourced fair", "A second life"];

const collections = [
  { no: "01", title: "Just in", body: "Freshly inspected and added to the floor this week.", href: "/products" },
  { no: "02", title: "Under $100", body: "Honest finds that go easy on the wallet.", href: "/products" },
  { no: "03", title: "Staff picks", body: "The pieces we'd happily take home ourselves.", href: "/products" },
];

const testimonials = [
  { quote: "Described the wear down to the scuff. No surprises when it showed up — rare these days.", name: "Marcus T.", place: "Sarasota, FL" },
  { quote: "The receiver was recapped exactly like the listing said. Sounds incredible, packed with care.", name: "Dana R.", place: "Tampa, FL" },
  { quote: "Picked up locally in Bradenton. Honest folks, fair price, zero games. I'll be back.", name: "Priya S.", place: "Bradenton, FL" },
];

export default async function HomePage() {
  const [items, categories] = await Promise.all([getListings(), getCategories()]);
  const featured = items[0]!;
  const floor = items.slice(0, 8);

  return (
    <div>
      {/* ── Hero — bold, art-directed, dark ───────────────── */}
      <section className="relative overflow-hidden bg-ink text-background">
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0"
          style={{ backgroundImage: "radial-gradient(50% 55% at 88% -8%, hsl(30 64% 52% / 0.24), transparent 60%)" }}
        />
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 opacity-[0.05]"
          style={{
            backgroundImage:
              "linear-gradient(hsl(40 36% 96%) 1px, transparent 1px), linear-gradient(90deg, hsl(40 36% 96%) 1px, transparent 1px)",
            backgroundSize: "76px 76px",
          }}
        />

        <div className="relative mx-auto max-w-7xl px-6 pb-16 pt-20 md:pt-24">
          <Reveal>
            <p className="flex items-center gap-3 font-mono text-[11px] uppercase tracking-[0.3em] text-copper">
              <span aria-hidden className="inline-block h-px w-10 bg-copper" />
              Est. 2026 · Bradenton, Florida · Secondhand goods, certified honest
            </p>
          </Reveal>

          <Reveal delay={90}>
            <h1 className="mt-8 font-heading text-[clamp(3rem,12vw,10rem)] font-semibold leading-[0.84] tracking-[-0.03em]">
              Good finds.
              <br />
              <span className="text-copper">No surprises.</span>
            </h1>
          </Reveal>

          <div className="mt-14 grid gap-12 lg:grid-cols-[1fr_minmax(0,26rem)] lg:items-end">
            <Reveal delay={170}>
              <p className="max-w-md text-lg leading-relaxed text-background/70">
                Secondhand furniture, electronics and tools — each piece inspected, graded straight, and
                certified honest before it ever hits the floor.
              </p>
              <div className="mt-8 flex flex-wrap items-center gap-5">
                <Link
                  href="/products"
                  className="group inline-flex items-center gap-2.5 bg-copper px-7 py-3.5 text-sm font-semibold uppercase tracking-[0.12em] text-ink transition-transform duration-300 hover:-translate-y-0.5"
                >
                  Shop the floor
                  <ArrowRight className="h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" aria-hidden />
                </Link>
                <Link
                  href="/grading"
                  className="inline-flex items-center border-b border-background/30 pb-1 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:border-copper hover:text-copper"
                >
                  How we grade
                </Link>
              </div>
              <dl className="mt-12 grid grid-cols-2 gap-x-8 gap-y-6 border-t border-background/15 pt-7 sm:grid-cols-4">
                {heroStats.map(([k, v]) => (
                  <div key={v}>
                    <dt className="font-heading text-2xl font-semibold text-background">{k}</dt>
                    <dd className="mt-0.5 font-mono text-[10px] uppercase tracking-[0.18em] text-background/55">{v}</dd>
                  </div>
                ))}
              </dl>
            </Reveal>

            {/* Featured certified lot */}
            <Reveal delay={240} className="relative">
              <div
                aria-hidden
                className="absolute -left-5 -top-7 z-20 flex h-28 w-28 -rotate-[14deg] flex-col items-center justify-center rounded-full border-2 border-copper bg-ink text-center shadow-elev-lg"
              >
                <span className="absolute inset-2 rounded-full border border-dashed border-copper/40" />
                <span className="font-mono text-[8px] uppercase tracking-[0.18em] text-copper">Inspected</span>
                <BadgeCheck className="my-1 h-6 w-6 text-copper" aria-hidden />
                <span className="font-mono text-[8px] uppercase tracking-[0.18em] text-copper">& Graded</span>
              </div>

              <Link href={`/products/${featured.slug}`} className="group block">
                <div className="relative aspect-[4/5] overflow-hidden bg-background/[0.04] ring-1 ring-background/15 transition-transform duration-500 ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:-translate-y-1.5">
                  {featured.image ? (
                    <Image
                      src={featured.image}
                      alt={featured.title}
                      fill
                      sizes="(max-width: 1024px) 100vw, 45vw"
                      className="object-cover transition-transform duration-[800ms] ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:scale-105"
                      priority
                    />
                  ) : (
                    <div className="absolute inset-0 flex items-center justify-center">
                      <span className="font-heading text-[9rem] text-background/[0.05] transition-transform duration-[800ms] ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:scale-105">
                        OT
                      </span>
                    </div>
                  )}
                  <span aria-hidden className="absolute right-4 top-4 h-7 w-7 border-r-2 border-t-2 border-copper" />
                  <div className="absolute inset-x-0 bottom-0 space-y-2 bg-gradient-to-t from-ink via-ink/80 to-transparent px-6 pb-6 pt-20">
                    <div className="flex items-center gap-2 font-mono text-[10px] uppercase tracking-[0.18em] text-copper">
                      <span>Lot of the week</span>
                      <span aria-hidden className="h-1 w-1 rounded-full bg-copper" />
                      <span>{featured.sku}</span>
                    </div>
                    <h2 className="font-heading text-2xl font-medium leading-snug text-background">{featured.title}</h2>
                    <div className="flex items-center gap-3 pt-1">
                      <span className="font-heading text-xl font-semibold text-background">${featured.price.toLocaleString()}</span>
                      {featured.compareAt ? (
                        <span className="font-mono text-xs text-background/50 line-through">${featured.compareAt.toLocaleString()}</span>
                      ) : null}
                      <span className="ml-auto inline-flex items-center gap-1.5 font-mono text-[10px] uppercase tracking-[0.18em] text-copper">
                        View dossier
                        <ArrowUpRight className="h-3.5 w-3.5 transition-transform duration-300 group-hover:translate-x-0.5 group-hover:-translate-y-0.5" aria-hidden />
                      </span>
                    </div>
                  </div>
                </div>
              </Link>
            </Reveal>
          </div>
        </div>

        {/* Brand-promise marquee */}
        <div className="relative flex overflow-hidden border-t border-background/12 bg-ink py-4">
          <div className="marquee-track flex shrink-0 items-center gap-12 whitespace-nowrap pr-12 font-mono text-[11px] uppercase tracking-[0.32em] text-copper">
            {[...ticker, ...ticker, ...ticker, ...ticker].map((t, i) => (
              <span key={i} className="flex items-center gap-12">
                {t}
                <span aria-hidden className="h-1 w-1 rounded-full bg-copper/50" />
              </span>
            ))}
          </div>
        </div>
      </section>

      {/* ── Principles (manifest) ────────────────────────── */}
      <section className="border-b border-rule bg-background">
        <div className="mx-auto max-w-7xl px-6 py-16">
          <div className="grid gap-x-10 gap-y-10 sm:grid-cols-2 lg:grid-cols-4">
            {principles.map((p, i) => (
              <Reveal key={p.title} delay={i * 80}>
                <div className="border-t border-copper/50 pt-5">
                  <div className="flex items-baseline gap-3">
                    <span className="font-mono text-xs text-copper">{String(i + 1).padStart(2, "0")}</span>
                    <h3 className="font-heading text-lg font-medium text-ink">{p.title}</h3>
                  </div>
                  <p className="mt-2.5 text-sm leading-relaxed text-slate">{p.body}</p>
                </div>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      {/* ── On the floor ─────────────────────────────────── */}
      <section className="mx-auto max-w-7xl px-6 py-24">
        <Reveal className="flex items-end justify-between">
          <div>
            <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">Latest finds</p>
            <h2 className="mt-3 font-heading text-4xl font-semibold tracking-[-0.02em] text-ink">On the floor</h2>
          </div>
          <Link
            href="/products"
            className="group hidden items-center gap-2 border-b border-ink/20 pb-1 font-mono text-[11px] uppercase tracking-[0.18em] text-slate transition-colors hover:border-copper hover:text-ink sm:inline-flex"
          >
            View all
            <ArrowRight className="h-3.5 w-3.5 transition-transform group-hover:translate-x-1" aria-hidden />
          </Link>
        </Reveal>
        <div className="mt-12 grid grid-cols-2 gap-x-6 gap-y-12 sm:grid-cols-3 lg:grid-cols-4">
          {floor.map((item, i) => (
            <Reveal key={item.id} delay={(i % 4) * 90}>
              <ProductCard item={item} />
            </Reveal>
          ))}
        </div>
      </section>

      {/* ── Curated collections (dark band) ──────────────── */}
      <section className="bg-ink text-background">
        <div className="mx-auto max-w-7xl px-6 py-20">
          <Reveal>
            <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">Curated</p>
            <h2 className="mt-3 font-heading text-4xl font-semibold tracking-[-0.02em] text-background">Ways to shop</h2>
          </Reveal>
          <div className="mt-10 grid gap-6 md:grid-cols-3">
            {collections.map((c, i) => (
              <Reveal key={c.title} delay={i * 90}>
                <Link
                  href={c.href}
                  className="group flex aspect-[5/4] flex-col justify-between bg-background/[0.04] p-7 ring-1 ring-background/15 transition-all duration-300 hover:bg-background/[0.07] hover:ring-copper"
                >
                  <span className="font-mono text-xs text-copper">{c.no}</span>
                  <div>
                    <h3 className="font-heading text-3xl font-medium text-background transition-colors group-hover:text-copper">
                      {c.title}
                    </h3>
                    <p className="mt-1.5 max-w-[24ch] text-sm leading-relaxed text-background/60">{c.body}</p>
                    <span className="mt-4 inline-flex items-center gap-2 font-mono text-[11px] uppercase tracking-[0.18em] text-copper">
                      Browse
                      <ArrowUpRight
                        className="h-3.5 w-3.5 transition-transform duration-300 group-hover:translate-x-0.5 group-hover:-translate-y-0.5"
                        aria-hidden
                      />
                    </span>
                  </div>
                </Link>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      {/* ── Categories (index) ───────────────────────────── */}
      <section className="border-y border-rule bg-surface/50">
        <div className="mx-auto max-w-7xl px-6 py-20">
          <Reveal className="flex items-end justify-between">
            <h2 className="font-heading text-3xl font-semibold tracking-[-0.02em] text-ink">Shop by category</h2>
            <span className="font-mono text-[11px] uppercase tracking-[0.18em] text-slate/55">
              {categories.length} departments
            </span>
          </Reveal>
          <div className="mt-10 grid gap-x-14 sm:grid-cols-2">
            {categories.map((cat, i) => (
              <Reveal key={cat.slug} delay={(i % 2) * 70}>
                <Link
                  href={`/categories/${cat.slug}`}
                  className="group flex items-baseline justify-between border-b border-rule py-6 transition-colors hover:border-ink"
                >
                  <span className="flex items-baseline gap-4">
                    <span className="font-mono text-xs text-slate/45">{String(i + 1).padStart(2, "0")}</span>
                    <span className="font-heading text-2xl font-medium text-ink transition-colors group-hover:text-copper">
                      {cat.name}
                    </span>
                  </span>
                  <span className="flex items-center gap-3 font-mono text-[11px] uppercase tracking-[0.18em] text-slate/55">
                    {String(cat.count).padStart(2, "0")} items
                    <ArrowUpRight
                      className="h-4 w-4 text-copper opacity-0 transition-all duration-300 group-hover:translate-x-0.5 group-hover:opacity-100"
                      aria-hidden
                    />
                  </span>
                </Link>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      {/* ── Social proof ─────────────────────────────────── */}
      <section className="mx-auto max-w-7xl px-6 py-24">
        <Reveal>
          <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">Word on the floor</p>
          <h2 className="mt-3 font-heading text-4xl font-semibold tracking-[-0.02em] text-ink">Bought with confidence</h2>
        </Reveal>
        <div className="mt-12 grid gap-10 md:grid-cols-3">
          {testimonials.map((t, i) => (
            <Reveal key={t.name} delay={i * 90}>
              <figure className="border-t-2 border-copper/40 pt-6">
                <blockquote className="font-heading text-xl font-medium leading-snug text-ink">
                  &ldquo;{t.quote}&rdquo;
                </blockquote>
                <figcaption className="mt-5 font-mono text-[11px] uppercase tracking-[0.16em] text-slate/70">
                  {t.name} · {t.place}
                </figcaption>
                <p className="mt-2 inline-flex items-center gap-1.5 font-mono text-[10px] uppercase tracking-[0.16em] text-copper">
                  <BadgeCheck className="h-3.5 w-3.5" aria-hidden /> Verified buyer
                </p>
              </figure>
            </Reveal>
          ))}
        </div>
      </section>

      {/* ── Services ─────────────────────────────────────── */}
      <section className="mx-auto max-w-7xl px-6 py-24">
        <div className="grid gap-x-10 gap-y-10 md:grid-cols-3">
          {services.map((svc, i) => (
            <Reveal key={svc.title} delay={i * 90}>
              <Link href={svc.href} className="group block border-t-2 border-ink/10 pt-6 transition-colors hover:border-copper">
                <span className="font-mono text-xs text-copper">{svc.no}</span>
                <h3 className="mt-3 font-heading text-2xl font-medium text-ink">{svc.title}</h3>
                <p className="mt-2.5 text-sm leading-relaxed text-slate">{svc.body}</p>
                <span className="mt-5 inline-flex items-center gap-2 font-mono text-[11px] uppercase tracking-[0.18em] text-ink">
                  Learn more
                  <ArrowRight className="h-3.5 w-3.5 text-copper transition-transform group-hover:translate-x-1" aria-hidden />
                </span>
              </Link>
            </Reveal>
          ))}
        </div>
      </section>

      {/* ── Newsletter (first dibs) ──────────────────────── */}
      <section className="border-t border-rule bg-surface/50">
        <div className="mx-auto max-w-3xl px-6 py-20 text-center">
          <Reveal>
            <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">First dibs</p>
            <h2 className="mt-3 font-heading text-[clamp(2rem,4vw,3rem)] font-semibold tracking-[-0.02em] text-ink">
              The good stuff goes fast.
            </h2>
            <p className="mx-auto mt-4 max-w-md leading-relaxed text-slate">
              New finds drop every week. Get them in your inbox before they hit the floor.
            </p>
            <NewsletterForm />
          </Reveal>
        </div>
      </section>
    </div>
  );
}
