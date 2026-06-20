import Link from "next/link";
import { Search } from "lucide-react";
import { ProductCard } from "@/components/store/product-card";
import { Reveal } from "@/components/ui/reveal";
import { getListings, getCategories } from "@/lib/catalog";

export const metadata = {
  title: "Shop the floor",
  description: "Quality secondhand furniture, electronics, tools and finds — each one inspected and graded straight.",
};

function pillClass(active: boolean) {
  return [
    "border px-4 py-2 font-mono text-[11px] uppercase tracking-[0.16em] transition-colors",
    active ? "border-ink bg-ink text-background" : "border-rule text-slate hover:border-ink/40 hover:text-ink",
  ].join(" ");
}

export default async function ProductsPage({
  searchParams,
}: {
  searchParams: Promise<{ category?: string; q?: string }>;
}) {
  const sp = await searchParams;
  const [items, categories] = await Promise.all([
    getListings({ category: sp.category, search: sp.q }),
    getCategories(),
  ]);
  const activeCat = categories.find((c) => c.slug === sp.category);
  const eyebrow = sp.q ? `Results for “${sp.q}”` : activeCat ? activeCat.name : "The catalogue";

  return (
    <div className="mx-auto max-w-7xl px-6 py-12 md:py-16">
      <Reveal className="flex flex-wrap items-end justify-between gap-4 border-b border-rule pb-6">
        <div>
          <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">{eyebrow}</p>
          <h1 className="mt-2 font-heading text-[clamp(2.5rem,5vw,4rem)] font-semibold tracking-[-0.02em] text-ink">
            The floor
          </h1>
        </div>
        <span className="font-mono text-[11px] uppercase tracking-[0.18em] text-slate/60">
          {items.length} {items.length === 1 ? "lot" : "lots"} live
        </span>
      </Reveal>

      <div className="flex flex-wrap items-center gap-2 py-7">
        <Link href="/products" className={pillClass(!sp.category && !sp.q)}>
          All
        </Link>
        {categories.map((cat) => (
          <Link key={cat.slug} href={`/products?category=${cat.slug}`} className={pillClass(sp.category === cat.slug)}>
            {cat.name}
          </Link>
        ))}

        <form
          action="/products"
          className="ml-auto flex items-center gap-2 border border-rule bg-background px-3 py-2 transition-colors focus-within:border-copper"
        >
          <Search className="h-4 w-4 text-slate" aria-hidden />
          <input
            name="q"
            defaultValue={sp.q ?? ""}
            placeholder="Search the floor…"
            aria-label="Search products"
            className="w-44 bg-transparent text-sm text-ink outline-none placeholder:text-slate/60"
          />
        </form>
      </div>

      {items.length > 0 ? (
        <div className="grid grid-cols-2 gap-x-6 gap-y-12 sm:grid-cols-3 lg:grid-cols-4">
          {items.map((item, i) => (
            <Reveal key={item.id} delay={(i % 4) * 70}>
              <ProductCard item={item} />
            </Reveal>
          ))}
        </div>
      ) : (
        <div className="bg-surface px-6 py-24 text-center ring-1 ring-rule">
          <p className="font-heading text-2xl text-ink">Nothing matches that yet.</p>
          <p className="mt-2 text-sm text-slate">
            New finds land regularly. Try another category, or{" "}
            <Link href="/products" className="text-copper underline-offset-4 hover:underline">
              browse everything
            </Link>
            .
          </p>
        </div>
      )}
    </div>
  );
}
