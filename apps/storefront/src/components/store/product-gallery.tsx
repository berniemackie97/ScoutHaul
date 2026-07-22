"use client";

import { useState } from "react";
import Image from "next/image";
import { BadgeCheck } from "lucide-react";

type ProductGalleryProps = {
  title: string;
  sku?: string;
  /** Absolute photo URLs (thumbnail endpoint). The gallery derives full-resolution URLs itself. */
  images: string[];
};

/** The photo endpoint serves a small thumbnail by default; `?size=full` returns the original upload. The main
 * display must always use the original — a thumbnail stretched into the hero box is what "blurry" looks like. */
function fullSize(url: string) {
  return `${url}${url.includes("?") ? "&" : "?"}size=full`;
}

/**
 * PDP image gallery: one large display, thumbnails swap it in place (never navigate away — a shopper mid-decision
 * should not be dumped into a raw image tab). Main display renders the full-resolution original, letterboxed rather
 * than cropped, because on a graded one-of-one item the photo IS the condition report — cropping it hides exactly
 * what the buyer needs to see.
 */
export function ProductGallery({ title, sku, images }: ProductGalleryProps) {
  const [selected, setSelected] = useState(0);
  const current = images[selected] ?? images[0];

  return (
    <div className="relative">
      {/* Certification seal */}
      <div
        aria-hidden
        className="absolute -left-4 -top-5 z-20 flex h-24 w-24 -rotate-[14deg] flex-col items-center justify-center rounded-full border-2 border-copper bg-ink text-center shadow-elev-lg"
      >
        <span className="absolute inset-1.5 rounded-full border border-dashed border-copper/40" />
        <span className="font-mono text-[8px] uppercase tracking-[0.16em] text-copper">Inspected</span>
        <BadgeCheck className="my-0.5 h-5 w-5 text-copper" aria-hidden />
        <span className="font-mono text-[8px] uppercase tracking-[0.16em] text-copper">& Graded</span>
      </div>

      <div className="relative aspect-[4/5] overflow-hidden bg-surface ring-1 ring-rule">
        {current ? (
          <Image
            key={current}
            src={fullSize(current)}
            alt={title}
            fill
            sizes="(max-width: 1024px) 100vw, 50vw"
            className="object-contain"
            priority={selected === 0}
          />
        ) : (
          <div className="absolute inset-0 flex items-center justify-center">
            <span className="font-heading text-[9rem] text-ink/[0.06]">OT</span>
          </div>
        )}
        {sku ? (
          <span className="absolute right-4 top-4 font-mono text-[11px] uppercase tracking-[0.18em] text-slate/60">
            {sku}
          </span>
        ) : null}
        <span aria-hidden className="absolute bottom-4 right-4 h-6 w-6 border-b-2 border-r-2 border-copper/60" />
      </div>

      {images.length > 1 ? (
        <div className="mt-3 grid grid-cols-4 gap-3 sm:grid-cols-5">
          {images.map((src, index) => (
            <button
              key={src}
              type="button"
              onClick={() => setSelected(index)}
              aria-label={`View image ${index + 1} of ${images.length}`}
              aria-current={index === selected}
              className={`relative aspect-square overflow-hidden bg-surface ring-1 transition-all ${
                index === selected ? "ring-2 ring-copper" : "ring-rule hover:ring-ink/30"
              }`}
            >
              <Image src={src} alt={`${title} — image ${index + 1}`} fill sizes="120px" className="object-cover" />
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
