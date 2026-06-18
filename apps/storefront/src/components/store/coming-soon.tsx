import Link from "next/link";
import { ArrowLeft } from "lucide-react";

export function ComingSoon({ title, note }: { title: string; note?: string }) {
  return (
    <div className="mx-auto max-w-2xl px-6 py-28 text-center">
      <p className="font-mono text-[11px] uppercase tracking-[0.25em] text-copper">Overhaul Trading Co.</p>
      <h1 className="mt-4 font-heading text-4xl font-semibold tracking-tight text-ink">{title}</h1>
      <p className="mt-4 leading-relaxed text-slate">
        {note ?? "This part of the shop is being built. Check back soon."}
      </p>
      <Link
        href="/products"
        className="mt-8 inline-flex items-center gap-2 bg-copper px-6 py-3 text-sm font-semibold uppercase tracking-wider text-white transition-colors hover:bg-brand-700"
      >
        <ArrowLeft className="h-4 w-4" aria-hidden /> Back to the floor
      </Link>
    </div>
  );
}
