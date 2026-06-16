import Link from "next/link";
import { ArrowRight, ShieldCheck, Tag, Truck, Recycle, Wrench, HandCoins } from "lucide-react";
import { ProductCard } from "@/components/store/product-card";
import { sampleSpecimens, sampleCategories } from "@/lib/sample-data";

const valueProps = [
  { icon: ShieldCheck, title: "Inspected & honest", body: "Every item tested and described as-is. Condition is in every listing — never buried." },
  { icon: Tag, title: "Sourced & priced fair", body: "Estate sales, liquidation, smart buys. Typically 50–60% below retail." },
  { icon: Truck, title: "Ships fast · local pickup", body: "Quick dispatch nationwide. Pickup available here in Bradenton." },
  { icon: Recycle, title: "A second life", body: "Quality goods kept in use. Better for your wallet, better for the planet." },
];

const services = [
  { icon: ShieldCheck, title: "Buy with confidence", body: "Every item is inspected and graded straight. If it isn't as described, send it back.", href: "/grading" },
  { icon: Wrench, title: "Repairs", body: "Electronics, furniture, and appliances brought back to working order at a fair rate.", href: "/repairs" },
  { icon: HandCoins, title: "Consignment", body: "Have something good you're done with? We'll sell it for you. Clear terms, fair split.", href: "/consignment" },
];

const featured = sampleSpecimens[0];

export default function HomePage() {
  return (
    <div>
      {/* ── Hero ─────────────────────────────────────────── */}
      <section className="border-b border-rule">
        <div className="mx-auto grid max-w-7xl gap-12 px-6 py-16 md:py-24 lg:grid-cols-[1.15fr_0.85fr] lg:items-center">
          <div>
            <p className="flex items-center gap-2 font-mono text-[11px] uppercase tracking-[0.25em] text-slate/80">
              <span aria-hidden className="inline-block h-2 w-2 bg-copper" />
              Est. 2026 · Bradenton, Florida
            </p>
            <h1 className="mt-6 font-heading text-5xl font-semibold leading-[1.04] tracking-tight text-ink md:text-7xl">
              Good finds.
              <br />
              <span className="text-copper">No surprises.</span>
            </h1>
            <p className="mt-6 max-w-xl text-lg leading-relaxed text-slate">
              Quality secondhand furniture, electronics, tools and the occasional great find — each
              one inspected, graded straight, and ready to use. What you see is what shows up.
            </p>
            <div className="mt-8 flex flex-wrap items-center gap-3">
              <Link
                href="/products"
                className="inline-flex items-center gap-2 bg-copper px-6 py-3 text-sm font-semibold uppercase tracking-wider text-white transition-colors hover:bg-brand-700"
              >
                Shop the floor
                <ArrowRight className="h-4 w-4" aria-hidden />
              </Link>
              <Link
                href="/grading"
                className="inline-flex items-center gap-2 border border-ink/30 px-6 py-3 text-sm font-semibold uppercase tracking-wider text-ink transition-colors hover:bg-surface"
              >
                How we grade
              </Link>
            </div>
            <dl className="mt-12 flex flex-wrap gap-x-10 gap-y-4 border-t border-rule pt-6 font-mono text-[11px] uppercase tracking-wider text-slate/80">
              <div><dt className="text-ink font-semibold text-base">247</dt><dd>items live</dd></div>
              <div><dt className="text-ink font-semibold text-base">Every</dt><dd>item inspected</dd></div>
              <div><dt className="text-ink font-semibold text-base">FL</dt><dd>ships from</dd></div>
              <div><dt className="text-ink font-semibold text-base">Pickup</dt><dd>in Bradenton</dd></div>
            </dl>
          </div>

          {/* Featured specimen */}
          <div className="relative border border-rule bg-surface">
            <span className="absolute left-0 top-0 z-10 bg-ink px-2.5 py-1 font-mono text-[10px] font-semibold uppercase tracking-widest text-white">
              Featured
            </span>
            <span aria-hidden className="absolute right-3 top-3 z-10 h-4 w-4 border-r border-t border-copper" />
            <div className="flex aspect-[4/3] items-center justify-center border-b border-rule">
              <span className="font-heading text-8xl text-ink/[0.08]">OT</span>
            </div>
            <div className="space-y-3 p-6">
              <div className="flex items-center gap-2 font-mono text-[11px] uppercase tracking-wider text-slate/80">
                <span>{featured.sku}</span>
                <span aria-hidden className="h-1 w-1 rounded-full bg-copper" />
                <span className="text-copper">{featured.condition}</span>
              </div>
              <h2 className="font-heading text-2xl font-medium leading-snug text-ink">{featured.title}</h2>
              <p className="text-sm leading-relaxed text-slate">{featured.blurb}</p>
              <div className="flex items-baseline gap-3 pt-1">
                <span className="text-2xl font-semibold text-ink">${featured.price.toLocaleString()}</span>
                {featured.compareAt ? (
                  <span className="text-slate/60 line-through">${featured.compareAt.toLocaleString()}</span>
                ) : null}
              </div>
              <Link
                href={`/products/${featured.slug}`}
                className="inline-flex items-center gap-1.5 pt-1 font-mono text-[12px] font-semibold uppercase tracking-wider text-copper hover:text-brand-700"
              >
                View dossier <ArrowRight className="h-3.5 w-3.5" aria-hidden />
              </Link>
            </div>
          </div>
        </div>
      </section>

      {/* ── Value strip ──────────────────────────────────── */}
      <section className="bg-ink">
        <div className="mx-auto grid max-w-7xl gap-8 px-6 py-12 sm:grid-cols-2 lg:grid-cols-4">
          {valueProps.map((prop) => (
            <div key={prop.title} className="flex gap-4">
              <prop.icon className="h-6 w-6 shrink-0 text-copper" aria-hidden />
              <div>
                <h3 className="font-heading text-base font-medium text-white">{prop.title}</h3>
                <p className="mt-1 text-sm leading-relaxed text-white/65">{prop.body}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

      {/* ── On the floor ─────────────────────────────────── */}
      <section className="mx-auto max-w-7xl px-6 py-20">
        <div className="flex items-end justify-between border-b border-rule pb-5">
          <div>
            <p className="font-mono text-[11px] uppercase tracking-[0.25em] text-copper">Latest finds</p>
            <h2 className="mt-2 font-heading text-3xl font-semibold tracking-tight text-ink">On the floor</h2>
          </div>
          <Link
            href="/products"
            className="hidden items-center gap-1.5 font-mono text-[12px] font-semibold uppercase tracking-wider text-slate hover:text-ink sm:inline-flex"
          >
            View all <ArrowRight className="h-3.5 w-3.5" aria-hidden />
          </Link>
        </div>
        <div className="mt-8 grid grid-cols-2 gap-px bg-rule sm:grid-cols-3 lg:grid-cols-4">
          {sampleSpecimens.map((item) => (
            <ProductCard key={item.id} item={item} />
          ))}
        </div>
      </section>

      {/* ── Categories ───────────────────────────────────── */}
      <section className="border-y border-rule bg-surface">
        <div className="mx-auto max-w-7xl px-6 py-16">
          <h2 className="font-heading text-2xl font-semibold tracking-tight text-ink">Shop by category</h2>
          <div className="mt-8 grid grid-cols-2 gap-px bg-rule md:grid-cols-3 lg:grid-cols-6">
            {sampleCategories.map((cat) => (
              <Link
                key={cat.slug}
                href={`/categories/${cat.slug}`}
                className="group flex flex-col gap-2 bg-background p-5 transition-colors hover:bg-surface"
              >
                <span className="font-mono text-[11px] uppercase tracking-wider text-slate/70">
                  {String(cat.count).padStart(2, "0")} items
                </span>
                <span className="font-heading text-lg font-medium leading-tight text-ink group-hover:text-copper">
                  {cat.name}
                </span>
              </Link>
            ))}
          </div>
        </div>
      </section>

      {/* ── Services ─────────────────────────────────────── */}
      <section className="mx-auto max-w-7xl px-6 py-20">
        <div className="grid gap-px bg-rule md:grid-cols-3">
          {services.map((svc) => (
            <Link key={svc.title} href={svc.href} className="group bg-background p-8 transition-colors hover:bg-surface">
              <svc.icon className="h-7 w-7 text-copper" aria-hidden />
              <h3 className="mt-4 font-heading text-xl font-medium text-ink">{svc.title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-slate">{svc.body}</p>
              <span className="mt-4 inline-flex items-center gap-1.5 font-mono text-[12px] font-semibold uppercase tracking-wider text-copper">
                Learn more <ArrowRight className="h-3.5 w-3.5 transition-transform group-hover:translate-x-0.5" aria-hidden />
              </span>
            </Link>
          ))}
        </div>
      </section>
    </div>
  );
}
