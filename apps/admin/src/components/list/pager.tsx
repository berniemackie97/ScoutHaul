"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { ChevronLeft, ChevronRight } from "lucide-react";

/**
 * Page controls that keep the rest of the query string.
 *
 * Paging used to live inside the inventory table, so every other list either had no paging at all or
 * would have had to reinvent it — and a list with no paging quietly shows the first page and calls
 * it everything.
 */
export function Pager({ page, pageCount, total, noun }: { page: number; pageCount: number; total: number; noun: string }) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  function goTo(next: number) {
    const params = new URLSearchParams(searchParams.toString());
    params.set("page", String(next));
    router.push(`${pathname}?${params.toString()}`);
  }

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 text-sm">
      <span className="text-muted-foreground">
        {total} {total === 1 ? noun : `${noun}s`}
        {pageCount > 1 ? ` · page ${page} of ${pageCount}` : ""}
      </span>
      {pageCount > 1 ? (
        <div className="flex gap-2">
          <button
            type="button"
            disabled={page <= 1}
            onClick={() => goTo(page - 1)}
            className="hover:bg-muted inline-flex h-8 items-center gap-1 rounded-lg border px-3 disabled:opacity-40"
          >
            <ChevronLeft className="h-4 w-4" /> Prev
          </button>
          <button
            type="button"
            disabled={page >= pageCount}
            onClick={() => goTo(page + 1)}
            className="hover:bg-muted inline-flex h-8 items-center gap-1 rounded-lg border px-3 disabled:opacity-40"
          >
            Next <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      ) : null}
    </div>
  );
}
