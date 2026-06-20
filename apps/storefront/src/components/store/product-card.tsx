import Link from "next/link";
import Image from "next/image";
import { ArrowUpRight } from "lucide-react";
import type { Specimen } from "@/lib/catalog";

export function ProductCard({ item }: { item: Specimen }) {
  return (
    <Link href={`/products/${item.slug}`} className="group block">
      {/* Lot caption — reads like a catalogue entry */}
      <div className="mb-2.5 flex items-center justify-between font-mono text-[10px] uppercase tracking-[0.18em] text-slate/60">
        <span>{item.sku ?? "—"}</span>
        <span className="text-copper">{item.condition}</span>
      </div>

      {/* Plate */}
      <div className="relative aspect-[4/5] overflow-hidden bg-surface ring-1 ring-rule transition-[box-shadow,transform] duration-500 ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:-translate-y-1 group-hover:shadow-elev-lg group-hover:ring-ink/25">
        {item.image ? (
          <Image
            src={item.image}
            alt={item.title}
            fill
            sizes="(max-width: 640px) 50vw, (max-width: 1024px) 33vw, 25vw"
            className="object-cover transition-transform duration-[800ms] ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:scale-[1.05]"
          />
        ) : (
          <div className="absolute inset-0 flex items-center justify-center">
            <span className="font-heading text-5xl text-ink/[0.06] transition-transform duration-[800ms] ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:scale-[1.08]">
              OT
            </span>
          </div>
        )}

        {/* Copper corner survey-mark draws on hover */}
        <span
          aria-hidden
          className="absolute right-3 top-3 h-6 w-6 border-r-2 border-t-2 border-copper opacity-0 transition-opacity duration-300 group-hover:opacity-100"
        />

        {/* Dossier hint slides up from the base */}
        <div className="absolute inset-x-0 bottom-0 flex translate-y-full items-center justify-between bg-ink/95 px-4 py-2.5 font-mono text-[10px] uppercase tracking-[0.18em] text-background backdrop-blur-sm transition-transform duration-300 ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:translate-y-0">
          <span>View dossier</span>
          <ArrowUpRight className="h-3.5 w-3.5" aria-hidden />
        </div>
      </div>

      {/* Meta */}
      <div className="mt-3.5 space-y-1.5">
        <p className="font-mono text-[10px] uppercase tracking-[0.18em] text-slate/55">
          {item.category}
          {item.era ? ` · ${item.era}` : ""}
        </p>
        <h3 className="font-heading text-[17px] font-medium leading-snug text-ink">
          <span className="bg-[linear-gradient(theme(colors.copper),theme(colors.copper))] bg-[length:0%_1px] bg-left-bottom bg-no-repeat pb-0.5 transition-[background-size] duration-300 ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:bg-[length:100%_1px]">
            {item.title}
          </span>
        </h3>
        <div className="flex items-baseline gap-2 pt-0.5">
          <span className="font-heading text-lg font-semibold text-ink">${item.price.toLocaleString()}</span>
          {item.compareAt ? (
            <span className="font-mono text-xs text-slate/45 line-through">${item.compareAt.toLocaleString()}</span>
          ) : null}
        </div>
      </div>
    </Link>
  );
}
