import Link from "next/link";
import { ArrowUpRight } from "lucide-react";
import { Reveal } from "@/components/ui/reveal";
import { getCategories } from "@/lib/catalog";

export const dynamic = "force-dynamic";

export const metadata = {
  title: "Categories",
  description: "Browse the floor by department — furniture, electronics, collectibles, tools and more.",
  alternates: { canonical: "/categories" },
};

export default async function CategoriesPage() {
  const categories = await getCategories();

  return (
    <div className="mx-auto max-w-7xl px-6 py-12 md:py-16">
      <Reveal className="flex flex-wrap items-end justify-between gap-4 border-b border-rule pb-6">
        <div>
          <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">Browse</p>
          <h1 className="mt-2 font-heading text-[clamp(2.5rem,5vw,4rem)] font-semibold tracking-[-0.02em] text-ink">
            Departments
          </h1>
        </div>
        <span className="font-mono text-[11px] uppercase tracking-[0.18em] text-slate/60">
          {categories.length} departments
        </span>
      </Reveal>

      <div className="mt-10 grid gap-x-14 sm:grid-cols-2">
        {categories.map((cat, i) => (
          <Reveal key={cat.slug} delay={(i % 2) * 70}>
            <Link
              href={`/products?category=${cat.slug}`}
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
  );
}
