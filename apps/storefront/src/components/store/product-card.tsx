import Link from "next/link";
import type { Specimen } from "@/lib/sample-data";

export function ProductCard({ item }: { item: Specimen }) {
  return (
    <Link
      href={`/products/${item.slug}`}
      className="group block border border-rule bg-background transition-colors hover:border-ink/40"
    >
      <div className="relative aspect-square overflow-hidden border-b border-rule bg-surface">
        <div className="absolute inset-0 flex items-center justify-center">
          <span className="font-heading text-6xl text-ink/[0.07] transition-transform duration-500 group-hover:scale-105">
            OT
          </span>
        </div>
        {item.sku ? (
          <span className="absolute left-2.5 top-2.5 font-mono text-[10px] uppercase tracking-wider text-slate/70">
            {item.sku}
          </span>
        ) : null}
        <span aria-hidden className="absolute bottom-2.5 right-2.5 h-3 w-3 border-b border-r border-copper/60" />
      </div>

      <div className="space-y-2 p-3.5">
        <div className="flex items-center gap-2 font-mono text-[10px] uppercase tracking-wider text-slate/70">
          {item.category ? (
            <>
              <span>{item.category}</span>
              <span aria-hidden className="h-1 w-1 rounded-full bg-copper" />
            </>
          ) : null}
          <span className="text-copper">{item.condition}</span>
        </div>
        <h3 className="font-heading text-[15px] font-medium leading-snug text-ink line-clamp-2">
          {item.title}
        </h3>
        <div className="flex items-baseline gap-2 pt-0.5">
          <span className="font-semibold text-ink">${item.price.toLocaleString()}</span>
          {item.compareAt ? (
            <span className="text-sm text-slate/60 line-through">${item.compareAt.toLocaleString()}</span>
          ) : null}
        </div>
      </div>
    </Link>
  );
}
