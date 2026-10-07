import Link from "next/link";

/** Page links for the catalogue. Keeps the category and search; shows the pages around this one plus the ends. */
export function CatalogPager({
  page,
  pageCount,
  href,
}: {
  page: number;
  pageCount: number;
  href: (page: number) => string;
}) {
  if (pageCount <= 1) return null;

  const pages = [...new Set([1, page - 1, page, page + 1, pageCount])].filter((entry) => entry >= 1 && entry <= pageCount).sort((a, b) => a - b);
  const item = "inline-flex h-10 min-w-10 items-center justify-center border px-3 font-mono text-[11px] uppercase tracking-[0.16em]";

  return (
    <nav aria-label="Catalogue pages" className="mt-14 flex flex-wrap items-center justify-center gap-2">
      {page > 1 && (
        <Link href={href(page - 1)} rel="prev" className={`${item} border-rule text-slate hover:border-ink/40 hover:text-ink`}>
          Previous
        </Link>
      )}
      {pages.map((entry, index) => (
        <span key={entry} className="flex items-center gap-2">
          {index > 0 && entry - pages[index - 1]! > 1 && <span className="text-slate/60" aria-hidden>…</span>}
          <Link
            href={href(entry)}
            aria-current={entry === page ? "page" : undefined}
            className={`${item} ${entry === page ? "border-ink bg-ink text-background" : "border-rule text-slate hover:border-ink/40 hover:text-ink"}`}
          >
            {entry}
          </Link>
        </span>
      ))}
      {page < pageCount && (
        <Link href={href(page + 1)} rel="next" className={`${item} border-rule text-slate hover:border-ink/40 hover:text-ink`}>
          Next
        </Link>
      )}
    </nav>
  );
}
