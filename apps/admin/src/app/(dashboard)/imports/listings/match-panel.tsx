"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import { ImageIcon, Search } from "lucide-react";
import { Input } from "@overhaul/ui";
import { loadMatchCandidates, type MatchCandidate } from "./match-actions";

const VERDICT_STYLE: Record<string, string> = {
  Agrees: "border-success-border bg-success-surface text-success",
  Partial: "border-warning-border bg-warning-surface text-warning",
  Disagrees: "border-destructive-border bg-destructive-surface text-destructive",
};

const VERDICT_MARK: Record<string, string> = { Agrees: "✓", Partial: "~", Disagrees: "✕" };

const CONFIDENCE_STYLE: Record<string, string> = {
  Strong: "text-success",
  Likely: "text-warning",
  Weak: "text-muted-foreground",
};

/**
 * The items this listing could be, ranked by the server's match score with the reasons beside each. Searching
 * scores everything that contains the words, so the right item is one query away even when it ranked low.
 */
export function MatchPanel({
  listingId,
  selectedId,
  onSelect,
}: {
  listingId: string;
  selectedId: string;
  onSelect: (candidate: MatchCandidate) => void;
}) {
  const [search, setSearch] = useState("");
  const [candidates, setCandidates] = useState<MatchCandidate[] | null>(null);
  const [message, setMessage] = useState("");
  const [pending, startTransition] = useTransition();
  const latest = useRef(0);

  useEffect(() => {
    const request = ++latest.current;
    const timer = setTimeout(
      () =>
        startTransition(async () => {
          const result = await loadMatchCandidates(listingId, search).catch(() => ({ ok: false, message: "Could not load matches. Check the connection and try again.", candidates: [] }));
          if (request !== latest.current) return;
          setCandidates(result.candidates);
          setMessage(result.message);
        }),
      search ? 250 : 0,
    );
    return () => clearTimeout(timer);
  }, [listingId, search]);

  return (
    <div className="space-y-3">
      <label className="relative block">
        <Search className="text-muted-foreground pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2" aria-hidden />
        <Input
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Search all items by title, brand, barcode or bin…"
          aria-label="Search all items"
          className="h-10 pl-9"
        />
      </label>

      {message && <p className="text-destructive text-sm">{message}</p>}
      {candidates === null && <p className="text-muted-foreground text-sm">Finding likely items…</p>}
      {candidates?.length === 0 && (
        <p className="text-muted-foreground text-sm">{search ? "No item matches that search." : "Nothing in inventory looks like this listing. Create an item from it, or search."}</p>
      )}

      <ul role="listbox" aria-label="Matching items" aria-busy={pending} className={`space-y-2 ${pending ? "opacity-70" : ""}`}>
        {candidates?.map((candidate) => {
          const selected = candidate.inventoryItemId === selectedId;
          const thumb = candidate.thumbnailUrl;
          const meta = [candidate.brand, candidate.size, candidate.storageLocation && `Bin ${candidate.storageLocation}`, candidate.status]
            .filter(Boolean)
            .join(" · ");
          return (
            <li key={candidate.inventoryItemId} role="option" aria-selected={selected}>
              <button
                type="button"
                onClick={() => onSelect(candidate)}
                className={`flex w-full items-start gap-3 rounded-lg border p-3 text-left transition-colors ${
                  selected ? "border-copper bg-copper/5 ring-copper/30 ring-2" : "bg-card hover:bg-muted/50"
                }`}
              >
                <span className="bg-muted flex h-12 w-12 shrink-0 items-center justify-center overflow-hidden rounded-md border">
                  {thumb ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img src={thumb} alt="" className="h-full w-full object-cover" />
                  ) : (
                    <ImageIcon className="text-muted-foreground h-4 w-4" aria-hidden />
                  )}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium">{candidate.title}</span>
                  {meta && <span className="text-muted-foreground block truncate text-xs">{meta}</span>}
                  <span className="mt-1.5 flex flex-wrap gap-1">
                    {(candidate.signals ?? []).map((signal) => (
                      <span
                        key={`${signal.kind}:${signal.text}`}
                        className={`rounded border px-1.5 text-[11px] font-semibold leading-5 ${VERDICT_STYLE[signal.verdict ?? "Partial"]}`}
                      >
                        {VERDICT_MARK[signal.verdict ?? "Partial"]} {signal.text}
                      </span>
                    ))}
                  </span>
                </span>
                <span className="shrink-0 text-right">
                  <span className={`block text-xl font-bold tabular-nums ${CONFIDENCE_STYLE[candidate.confidence ?? "Weak"]}`}>{candidate.score}</span>
                  <span className="text-muted-foreground block text-[11px]">{candidate.confidence}</span>
                </span>
              </button>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
