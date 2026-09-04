import Link from "next/link";

/**
 * What a shopper sees when a page cannot be shown.
 *
 * Shared so adding a boundary is a few lines rather than a reason to skip one — the shop had none at
 * all, and any failure landed on Next's unbranded default.
 */
export function ShopMessage({
  eyebrow,
  title,
  body,
  action,
}: {
  eyebrow: string;
  title: string;
  body: string;
  action?: React.ReactNode;
}) {
  return (
    <section className="mx-auto flex min-h-[60vh] max-w-2xl flex-col justify-center px-6 py-16 text-center">
      <p className="font-mono text-[11px] uppercase tracking-[0.24em] text-copper">{eyebrow}</p>
      <h1 className="mt-4 font-serif text-3xl md:text-4xl">{title}</h1>
      <p className="mx-auto mt-4 max-w-prose text-sm leading-relaxed text-ink/70">{body}</p>
      <div className="mt-8 flex flex-wrap justify-center gap-3">
        {action}
        <Link
          href="/products"
          className="border border-rule px-5 py-2 font-mono text-[12px] uppercase tracking-[0.16em] transition hover:bg-ink hover:text-background"
        >
          Browse the shop
        </Link>
      </div>
    </section>
  );
}
