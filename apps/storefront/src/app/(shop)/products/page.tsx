import Link from "next/link";
import { Search } from "lucide-react";
import { ProductCard } from "@/components/store/product-card";
import { getListings, getCategories } from "@/lib/catalog";

export const metadata = {
  title: "Shop the floor",
  description: "Quality secondhand furniture, electronics, tools and finds — each one inspected and graded straight.",
};

function pill(active: boolean) {
  return [
    "border px-3.5 py-1.5 font-mono text-[11px] uppercase tracking-wider transition-colors",
    active
      ? "border-ink bg-ink text-white"
      : "border-rule text-slate hover:border-ink/40 hover:text-ink",
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

  return (
    <div className="mx-auto max-w-7xl px-6 py-12">
      <div className="border-b border-rule pb-6">
        <p className="font-mono text-[11px] uppercase tracking-[0.25em] text-copper">
          {items.length} {items.length === 1 ? "item" : "items"} live
        </p>
        <h1 className="mt-2 font-heading text-4xl font-semibold tracking-tight text-ink">
          The floor
        </h1>
      </div>

      <div className="flex flex-wrap items-center gap-2 py-6">
        <Link href="/products" className={pill(!sp.category)}>
          All
        </Link>
        {categories.map((cat) => (
          <Link key={cat.slug} href={`/products?category=${cat.slug}`} className={pill(sp.category === cat.slug)}>
            {cat.name}
          </Link>
        ))}

        <form action="/products" className="ml-auto flex items-center gap-2 border border-rule bg-background px-3 py-2 focus-within:border-copper">
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
        <div className="grid grid-cols-2 gap-px bg-rule sm:grid-cols-3 lg:grid-cols-4">
          {items.map((item) => (
            <ProductCard key={item.id} item={item} />
          ))}
        </div>
      ) : (
        <div className="border border-rule bg-surface px-6 py-20 text-center">
          <p className="font-heading text-xl text-ink">Nothing matches that yet.</p>
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
