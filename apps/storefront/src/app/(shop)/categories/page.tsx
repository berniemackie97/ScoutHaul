import Link from "next/link";
import { getCategories } from "@/lib/catalog";

export const metadata = { title: "Categories" };

export default async function CategoriesPage() {
  const categories = await getCategories();
  return (
    <div className="mx-auto max-w-7xl px-6 py-12">
      <div className="border-b border-rule pb-6">
        <p className="font-mono text-[11px] uppercase tracking-[0.25em] text-copper">Browse</p>
        <h1 className="mt-2 font-heading text-4xl font-semibold tracking-tight text-ink">Categories</h1>
      </div>
      <div className="mt-8 grid grid-cols-2 gap-px bg-rule md:grid-cols-3 lg:grid-cols-4">
        {categories.map((category) => (
          <Link
            key={category.slug}
            href={`/products?category=${category.slug}`}
            className="group flex flex-col gap-2 bg-background p-6 transition-colors hover:bg-surface"
          >
            <span className="font-mono text-[11px] uppercase tracking-wider text-slate/70">
              {String(category.count).padStart(2, "0")} items
            </span>
            <span className="font-heading text-xl font-medium text-ink group-hover:text-copper">{category.name}</span>
          </Link>
        ))}
      </div>
    </div>
  );
}
