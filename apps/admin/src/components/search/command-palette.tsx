"use client";

import { useRouter } from "next/navigation";
import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent } from "react";
import {
  AlertCircle,
  ArrowRight,
  Loader2,
  Package,
  Plus,
  ReceiptText,
  Search,
  ShoppingCart,
  Users,
  type LucideIcon,
} from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogTitle, cn } from "@overhaul/ui";
import { navGroups } from "@/components/layout/navigation";
import {
  MINIMUM_SEARCH_LENGTH,
  buildEntries,
  isAnswerFor,
  stepIndex,
  type PaletteEntry,
  type SearchResults,
} from "@/features/search/palette-model";
import type { Granted } from "@/lib/permissions";

/** Long enough to skip the keystrokes of a word being typed; short enough to feel immediate. */
const DEBOUNCE_MS = 150;

const HIT_ICONS: Record<string, LucideIcon> = {
  inventory: Package,
  order: ShoppingCart,
  customer: Users,
  purchase: ReceiptText,
};

const NAV_ICONS = new Map<string, LucideIcon>(navGroups.flatMap((group) => group.items.map((item) => [item.href, item.icon])));

/**
 * One box that finds anything and goes anywhere — ⌘K from every screen.
 *
 * The portal had search on two of its twenty-eight pages, so finding a record meant first knowing
 * which list it lived in. This searches inventory, orders, customers and purchases at once, and
 * offers every page and the common tasks alongside, so the keyboard reaches most of the portal
 * without the sidebar.
 *
 * Three failure modes are handled deliberately:
 * - a slow answer to an older query cannot overwrite a newer one (aborted, and ignored if it lands);
 * - a failed search says it failed, instead of rendering as "no matches", which would be a lie;
 * - from the first keystroke until the answer for exactly that text arrives, the palette says it is
 *   searching. The debounce and the round trip are both invisible otherwise, and an empty panel in
 *   that gap reads as "nothing found".
 */
export function CommandPalette({ open, onOpenChange, granted }: { open: boolean; onOpenChange: (open: boolean) => void; granted: Granted }) {
  const router = useRouter();
  const listId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchResults | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [activeKey, setActiveKey] = useState<string | null>(null);

  const term = query.trim();
  const searching = term.length >= MINIMUM_SEARCH_LENGTH;

  useEffect(() => {
    if (!open || !searching) return;

    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      try {
        const response = await fetch(`/api/search?q=${encodeURIComponent(term)}`, {
          signal: controller.signal,
          cache: "no-store",
        });
        const body: unknown = await response.json().catch(() => null);
        if (controller.signal.aborted) return;

        if (!response.ok) {
          setError(errorFrom(body) ?? "Search is unavailable right now.");
          return;
        }

        setResults(body as SearchResults);
        setError(null);
      } catch {
        if (controller.signal.aborted) return;
        setError("Search could not reach the server. Check the connection and try again.");
      }
    }, DEBOUNCE_MS);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [open, searching, term]);

  const entries = useMemo(() => buildEntries(query, results, granted), [query, results, granted]);

  // Derived from whether the results on hand answer this exact text — not from a loading flag,
  // which only flips after the debounce and so leaves a silent gap at the start of every search.
  const answered = searching && isAnswerFor(results, term);
  const pending = searching && !error && !answered;
  const noMatches = searching && !error && answered && entries.length === 0;

  // Selection is held by key, not position, so results landing under the cursor keep it on the
  // same row when that row survives — and fall back to the first row when it does not.
  const found = entries.findIndex((entry) => entry.key === activeKey);
  const activeIndex = found >= 0 ? found : entries.length > 0 ? 0 : -1;
  const active = activeIndex >= 0 ? entries[activeIndex] : undefined;

  useEffect(() => {
    if (!active) return;
    document.getElementById(optionId(listId, active.key))?.scrollIntoView({ block: "nearest" });
  }, [active, listId]);

  function close() {
    onOpenChange(false);
  }

  function handleOpenChange(next: boolean) {
    if (!next) {
      // Every open starts clean. A stale query from an hour ago is noise, not a convenience.
      setQuery("");
      setResults(null);
      setError(null);
      setActiveKey(null);
    }
    onOpenChange(next);
  }

  function go(entry: PaletteEntry, inNewTab: boolean) {
    const href = entry.type === "command" ? entry.command.href : entry.href;
    if (inNewTab) {
      window.open(href, "_blank", "noopener");
      return;
    }
    handleOpenChange(false);
    router.push(href);
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const next = stepIndex(activeIndex, event.key === "ArrowDown" ? 1 : -1, entries.length);
      setActiveKey(entries[next]?.key ?? null);
      return;
    }

    if (event.key === "Enter" && active) {
      event.preventDefault();
      go(active, event.metaKey || event.ctrlKey);
    }
  }

  function onRowClick(event: MouseEvent, entry: PaletteEntry) {
    go(entry, event.metaKey || event.ctrlKey);
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent
        className="top-[12vh] max-w-2xl translate-y-0 gap-0 overflow-hidden p-0 data-[state=closed]:slide-out-to-top-[10vh] data-[state=open]:slide-in-from-top-[10vh]"
        onOpenAutoFocus={(event) => {
          event.preventDefault();
          inputRef.current?.focus();
        }}
      >
        <DialogTitle className="sr-only">Search and commands</DialogTitle>
        <DialogDescription className="sr-only">
          Search inventory, orders, customers and purchases, or jump to a page. Use the arrow keys to move and Enter to open.
        </DialogDescription>

        <div className="flex h-12 items-center gap-3 border-b pl-4 pr-12">
          {pending ? (
            <Loader2 className="text-muted-foreground h-4 w-4 shrink-0 animate-spin" aria-hidden />
          ) : (
            <Search className="text-muted-foreground h-4 w-4 shrink-0" aria-hidden />
          )}
          <input
            ref={inputRef}
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setActiveKey(null);
              // A failure belongs to the text that failed. New text is a new question.
              setError(null);
              if (event.target.value.trim().length < MINIMUM_SEARCH_LENGTH) setResults(null);
            }}
            onKeyDown={onKeyDown}
            placeholder="Search items, orders, customers, purchases — or jump to a page"
            className="placeholder:text-muted-foreground h-full w-full bg-transparent text-sm outline-none"
            role="combobox"
            aria-expanded="true"
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={active ? optionId(listId, active.key) : undefined}
            autoComplete="off"
            spellCheck={false}
          />
        </div>

        <div id={listId} role="listbox" aria-label="Results" className="max-h-[min(60vh,28rem)] overflow-y-auto p-2">
          {error ? (
            <p role="alert" className="text-destructive flex items-center gap-2 px-3 py-3 text-sm">
              <AlertCircle className="h-4 w-4 shrink-0" aria-hidden /> {error}
            </p>
          ) : null}

          {noMatches ? (
            <p className="text-muted-foreground px-3 py-6 text-center text-sm">
              Nothing matches &ldquo;{term}&rdquo;.
            </p>
          ) : null}

          {pending && entries.length === 0 ? (
            <p role="status" className="text-muted-foreground flex items-center justify-center gap-2 px-3 py-6 text-sm">
              <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Searching for &ldquo;{term}&rdquo;…
            </p>
          ) : null}

          {sectioned(entries).map(({ section, rows }) => (
            <div key={section} role="presentation" className="mb-1 last:mb-0">
              <p role="presentation" className="text-muted-foreground/80 px-3 pb-1 pt-2 text-[11px] font-semibold uppercase tracking-wider">
                {section}
              </p>
              {rows.map((entry) => (
                <Row
                  key={entry.key}
                  id={optionId(listId, entry.key)}
                  entry={entry}
                  active={entry.key === active?.key}
                  onHover={() => setActiveKey(entry.key)}
                  onClick={(event) => onRowClick(event, entry)}
                />
              ))}
            </div>
          ))}
        </div>

        <div className="text-muted-foreground flex items-center justify-between gap-3 border-t px-4 py-2 text-[11px]">
          <span className="hidden sm:inline">
            <Kbd>↑</Kbd> <Kbd>↓</Kbd> move · <Kbd>↵</Kbd> open · <Kbd>⌘</Kbd>
            <Kbd>↵</Kbd> new tab · <Kbd>esc</Kbd> close
          </span>
          <button type="button" onClick={close} className="hover:text-foreground sm:hidden">
            Close
          </button>
          {answered && !error ? <span className="tabular-nums">{countLabel(results)}</span> : null}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function Row({
  id,
  entry,
  active,
  onHover,
  onClick,
}: {
  id: string;
  entry: PaletteEntry;
  active: boolean;
  onHover: () => void;
  onClick: (event: MouseEvent) => void;
}) {
  const Icon: LucideIcon =
    entry.type === "hit"
      ? (HIT_ICONS[entry.hit.kind ?? ""] ?? Search)
      : entry.type === "see-all"
        ? ArrowRight
        : entry.command.section === "Actions"
          ? Plus
          : (NAV_ICONS.get(entry.command.href) ?? ArrowRight);

  const label = entry.type === "hit" ? entry.hit.label : entry.type === "see-all" ? entry.label : entry.command.label;
  const sublabel = entry.type === "hit" ? entry.hit.sublabel : null;
  const badge = entry.type === "hit" ? entry.hit.badge : null;

  return (
    <div
      id={id}
      role="option"
      aria-selected={active}
      onMouseMove={onHover}
      onClick={onClick}
      className={cn(
        "flex cursor-pointer select-none items-center gap-3 rounded-lg px-3 py-2 text-sm",
        active ? "bg-brand-100 text-brand-900 dark:bg-brand-900 dark:text-brand-50" : "text-foreground",
      )}
    >
      <Icon className={cn("h-4 w-4 shrink-0", active ? "" : "text-muted-foreground")} aria-hidden />
      <span className="min-w-0 flex-1">
        <span className={cn("block truncate", entry.type === "see-all" && "text-brand-700 dark:text-brand-200")}>{label}</span>
        {sublabel ? <span className="text-muted-foreground block truncate text-xs">{sublabel}</span> : null}
      </span>
      {badge ? (
        <span className="text-muted-foreground shrink-0 rounded-full border px-2 py-0.5 text-[10px] font-medium">
          {humanize(badge)}
        </span>
      ) : null}
    </div>
  );
}

function Kbd({ children }: { children: React.ReactNode }) {
  return <kbd className="bg-muted rounded border px-1 font-sans text-[10px]">{children}</kbd>;
}

/**
 * "6 records", or "6+ records" when a group was cut off — a bare count beside a "See all" link
 * reads as the total, and it is only what fits.
 */
function countLabel(results: SearchResults | null): string {
  const shown = results?.totalHits ?? 0;
  const more = (results?.groups ?? []).some((group) => group.truncated);
  return `${shown}${more ? "+" : ""} ${shown === 1 && !more ? "record" : "records"}`;
}

/** Consecutive entries grouped under their heading, in the same order the keyboard walks them. */
function sectioned(entries: PaletteEntry[]): { section: string; rows: PaletteEntry[] }[] {
  const sections: { section: string; rows: PaletteEntry[] }[] = [];
  for (const entry of entries) {
    const last = sections.at(-1);
    if (last && last.section === entry.section) last.rows.push(entry);
    else sections.push({ section: entry.section, rows: [entry] });
  }
  return sections;
}

/** "ReadyToList" → "Ready to list". Status enums reach the UI as their C# names. */
function humanize(value: string): string {
  const spaced = value.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

function optionId(listId: string, key: string): string {
  return `${listId}-${key.replace(/[^a-zA-Z0-9_-]/g, "_")}`;
}

function errorFrom(body: unknown): string | null {
  return body && typeof body === "object" && "error" in body && typeof body.error === "string" ? body.error : null;
}
