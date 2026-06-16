import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, ShieldCheck, Truck, RotateCcw, MapPin } from "lucide-react";
import { ProductCard } from "@/components/store/product-card";
import { getListing, getRelated } from "@/lib/catalog";

export async function generateMetadata({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const item = await getListing(slug);
  return {
    title: item ? item.title : "Not found",
    description: item?.blurb,
  };
}

export default async function ProductPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const item = await getListing(slug);
  if (!item) notFound();

  const related = await getRelated(slug);

  const spec: [string, string | undefined][] = [
    ["Condition", item.condition],
    ["Brand", item.brand],
    ["Era", item.era],
    ["Category", item.category],
    ["Item no.", item.sku],
    ["Location", "Bradenton, FL"],
  ];

  return (
    <div className="mx-auto max-w-7xl px-6 py-10">
      <Link
        href="/products"
        className="inline-flex items-center gap-1.5 font-mono text-[11px] uppercase tracking-wider text-slate transition-colors hover:text-ink"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden /> Back to the floor
      </Link>

      <div className="mt-6 grid gap-10 lg:grid-cols-[1.1fr_0.9fr]">
        {/* Gallery */}
        <div>
          <div className="relative flex aspect-square items-center justify-center border border-rule bg-surface">
            <span className="font-heading text-9xl text-ink/[0.07]">OT</span>
            <span className="absolute left-3 top-3 font-mono text-[11px] uppercase tracking-wider text-slate/70">
              {item.sku}
            </span>
            <span aria-hidden className="absolute bottom-3 right-3 h-5 w-5 border-b border-r border-copper/50" />
          </div>
          <div className="mt-3 grid grid-cols-4 gap-3">
            {[0, 1, 2, 3].map((i) => (
              <div key={i} className="flex aspect-square items-center justify-center border border-rule bg-surface">
                <span className="font-heading text-2xl text-ink/[0.06]">OT</span>
              </div>
            ))}
          </div>
        </div>

        {/* Dossier */}
        <div>
          <div className="flex items-center gap-2 font-mono text-[11px] uppercase tracking-wider text-slate/80">
            <Link href={`/products?category=${item.category.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`} className="hover:text-ink">
              {item.category}
            </Link>
            <span aria-hidden className="h-1 w-1 rounded-full bg-copper" />
            <span className="text-copper">{item.condition}</span>
          </div>

          <h1 className="mt-3 font-heading text-4xl font-semibold leading-tight tracking-tight text-ink">
            {item.title}
          </h1>
          {item.brand ? <p className="mt-1.5 text-sm text-slate">{item.brand}</p> : null}

          <div className="mt-5 flex items-baseline gap-3">
            <span className="text-3xl font-semibold text-ink">${item.price.toLocaleString()}</span>
            {item.compareAt ? (
              <span className="text-lg text-slate/60 line-through">${item.compareAt.toLocaleString()}</span>
            ) : null}
          </div>

          <p className="mt-5 leading-relaxed text-slate">{item.blurb}</p>

          <div className="mt-7 flex flex-col gap-3 sm:flex-row">
            <button className="flex-1 bg-copper px-6 py-3.5 text-sm font-semibold uppercase tracking-wider text-white transition-colors hover:bg-brand-700">
              Add to cart
            </button>
            <button className="flex-1 border border-ink/30 px-6 py-3.5 text-sm font-semibold uppercase tracking-wider text-ink transition-colors hover:bg-surface">
              Make an offer
            </button>
          </div>

          {/* The dossier — honest spec sheet */}
          <div className="mt-8 border border-rule">
            <div className="border-b border-rule bg-ink px-4 py-2.5">
              <span className="font-mono text-[11px] font-semibold uppercase tracking-widest text-white">
                The dossier
              </span>
            </div>
            <dl className="divide-y divide-rule">
              {spec
                .filter(([, value]) => value)
                .map(([label, value]) => (
                  <div key={label} className="flex items-center justify-between px-4 py-2.5">
                    <dt className="font-mono text-[11px] uppercase tracking-wider text-slate/80">{label}</dt>
                    <dd className="font-mono text-[12px] text-ink">{value}</dd>
                  </div>
                ))}
            </dl>
          </div>

          {/* Trust */}
          <ul className="mt-6 space-y-2.5 text-sm text-slate">
            <li className="flex items-center gap-2.5"><ShieldCheck className="h-4 w-4 text-copper" aria-hidden /> Inspected and graded straight — condition is exactly as listed.</li>
            <li className="flex items-center gap-2.5"><RotateCcw className="h-4 w-4 text-copper" aria-hidden /> Easy returns if it arrives not as described.</li>
            <li className="flex items-center gap-2.5"><Truck className="h-4 w-4 text-copper" aria-hidden /> Ships nationwide. Free over $100.</li>
            <li className="flex items-center gap-2.5"><MapPin className="h-4 w-4 text-copper" aria-hidden /> Local pickup available in Bradenton, FL.</li>
          </ul>
        </div>
      </div>

      {/* Related */}
      {related.length > 0 ? (
        <section className="mt-20">
          <h2 className="border-b border-rule pb-4 font-heading text-2xl font-semibold tracking-tight text-ink">
            More from the floor
          </h2>
          <div className="mt-6 grid grid-cols-2 gap-px bg-rule sm:grid-cols-3 lg:grid-cols-4">
            {related.map((rel) => (
              <ProductCard key={rel.id} item={rel} />
            ))}
          </div>
        </section>
      ) : null}
    </div>
  );
}
