import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, BadgeCheck, ShieldCheck, Truck, RotateCcw, MapPin } from "lucide-react";
import { ProductGallery } from "@/components/store/product-gallery";
import { ProductCard } from "@/components/store/product-card";
import { ProductCommercePanel } from "@/components/store/product-commerce-panel";
import { Reveal } from "@/components/ui/reveal";
import { getListing, getRelated } from "@/lib/catalog";

export const dynamic = "force-dynamic";

export async function generateMetadata({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const item = await getListing(slug);
  return {
    title: item ? item.title : "Not found",
    description: item ? formatDescription(item.blurb).replace(/\n+/g, " ").slice(0, 160) : undefined,
  };
}

/** Imported descriptions carry Windows line endings and doubled blank lines; normalize so the text keeps its
 * written structure without rendering as triple-spaced or, worse, as one collapsed wall. */
function formatDescription(raw: string): string {
  return raw
    .replace(/\r\n/g, "\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}

const trust = [
  { icon: ShieldCheck, body: "Inspected and graded straight — condition is exactly as listed." },
  { icon: RotateCcw, body: "Easy returns if it arrives not as described." },
  { icon: Truck, body: "Ships nationwide. Shipping and tax are confirmed before payment." },
  { icon: MapPin, body: "Local pickup available in Bradenton, FL." },
];

export default async function ProductPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const item = await getListing(slug);
  if (!item) notFound();

  const related = await getRelated(slug);
  const categorySlug = item.category.toLowerCase().replace(/[^a-z0-9]+/g, "-");
  const savings = item.compareAt ? Math.round((1 - item.price / item.compareAt) * 100) : null;

  const spec: [string, string | undefined][] = [
    ["Condition", item.condition],
    ["Brand", item.brand],
    ["Model", item.model],
    ["Color", item.color],
    ["Size", item.size],
    ["Functional status", item.functionalStatus],
    ["Completeness", item.completenessStatus],
    ["Packaging", item.packagingStatus],
    ["Manufacture year", item.manufactureYear?.toString()],
    ["Country of origin", item.countryOfOrigin],
    ["Dimensions", item.dimensions],
    ["Weight", item.weight],
    ["Era", item.era],
    ["Category", item.category],
    ["Item no.", item.sku],
    ["Location", "Bradenton, FL"],
  ];

  return (
    <div className="mx-auto max-w-7xl px-6 py-10 md:py-14">
      <Link
        href="/products"
        className="group inline-flex items-center gap-1.5 font-mono text-[11px] uppercase tracking-[0.18em] text-slate transition-colors hover:text-ink"
      >
        <ArrowLeft className="h-3.5 w-3.5 transition-transform group-hover:-translate-x-0.5" aria-hidden /> Back to the floor
      </Link>

      <div className="mt-8 grid gap-12 lg:grid-cols-[1.05fr_0.95fr] lg:gap-16">
        {/* Gallery */}
        <div className="lg:sticky lg:top-28 lg:self-start">
          <ProductGallery
            title={item.title}
            sku={item.sku}
            images={item.images?.length ? item.images : item.image ? [item.image] : []}
          />
        </div>

        {/* Dossier */}
        <div>
          <Reveal>
            <div className="flex items-center gap-2 font-mono text-[11px] uppercase tracking-[0.18em] text-slate/70">
              <Link href={`/products?category=${categorySlug}`} className="transition-colors hover:text-ink">
                {item.category}
              </Link>
              <span aria-hidden className="h-1 w-1 rounded-full bg-copper" />
              <span className="text-copper">{item.condition}</span>
            </div>

            <h1 className="mt-3 font-heading text-[clamp(2rem,4vw,3.25rem)] font-semibold leading-[1.05] tracking-[-0.02em] text-ink">
              {item.title}
            </h1>
            {item.brand ? <p className="mt-2 font-mono text-xs uppercase tracking-[0.18em] text-slate/70">{item.brand}</p> : null}

            <div className="mt-6 flex flex-wrap items-baseline gap-3">
              <span className="font-heading text-3xl font-semibold text-ink">${item.price.toLocaleString()}</span>
              {item.compareAt ? (
                <span className="font-mono text-base text-slate/45 line-through">${item.compareAt.toLocaleString()}</span>
              ) : null}
              {savings && savings > 0 ? (
                <span className="bg-copper/10 px-2.5 py-1 font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-copper">
                  {savings}% below retail
                </span>
              ) : null}
            </div>

            <p className="mt-6 whitespace-pre-line leading-relaxed text-slate">{formatDescription(item.blurb)}</p>

            {/* Certified grade callout — the signature */}
            <div className="mt-7 flex items-center gap-4 border-l-2 border-copper bg-copper/[0.05] px-5 py-4">
              <BadgeCheck className="h-7 w-7 shrink-0 text-copper" aria-hidden />
              <div>
                <p className="font-mono text-[10px] uppercase tracking-[0.18em] text-copper">Inspected &amp; graded</p>
                <p className="mt-0.5 font-heading text-[15px] text-ink">Condition is exactly as listed — no surprises.</p>
              </div>
            </div>

            <ProductCommercePanel
              listingId={item.id}
              title={item.title}
              price={item.price}
              availability={item.availability}
              acceptsOffers={item.acceptsOffers}
              minimumOfferAmount={item.minimumOfferAmount}
              maximumOfferAmount={item.maximumOfferAmount}
            />

            {/* The dossier — honest spec sheet */}
            <div className="mt-9 ring-1 ring-rule">
              <div className="bg-ink px-4 py-2.5">
                <span className="font-mono text-[11px] font-semibold uppercase tracking-[0.2em] text-background">
                  The dossier
                </span>
              </div>
              <dl className="divide-y divide-rule">
                {spec
                  .filter(([, value]) => value)
                  .map(([label, value]) => (
                    <div key={label} className="flex items-center justify-between px-4 py-3">
                      <dt className="font-mono text-[11px] uppercase tracking-[0.16em] text-slate/70">{label}</dt>
                      <dd className="font-mono text-[12px] text-ink">{value}</dd>
                    </div>
                  ))}
              </dl>
            </div>

            <ul className="mt-7 space-y-3 text-sm text-slate">
              {trust.map((t) => (
                <li key={t.body} className="flex items-center gap-3">
                  <t.icon className="h-4 w-4 shrink-0 text-copper" aria-hidden />
                  {t.body}
                </li>
              ))}
            </ul>
          </Reveal>
        </div>
      </div>

      {/* Related */}
      {related.length > 0 ? (
        <section className="mt-24">
          <Reveal className="flex items-end justify-between border-b border-rule pb-5">
            <h2 className="font-heading text-3xl font-semibold tracking-[-0.02em] text-ink">More from the floor</h2>
            <Link
              href="/products"
              className="group hidden items-center gap-2 font-mono text-[11px] uppercase tracking-[0.18em] text-slate transition-colors hover:text-ink sm:inline-flex"
            >
              View all
            </Link>
          </Reveal>
          <div className="mt-10 grid grid-cols-2 gap-x-6 gap-y-12 sm:grid-cols-3 lg:grid-cols-4">
            {related.map((rel, i) => (
              <Reveal key={rel.id} delay={(i % 4) * 80}>
                <ProductCard item={rel} />
              </Reveal>
            ))}
          </div>
        </section>
      ) : null}
    </div>
  );
}
