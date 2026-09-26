"use client";

import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { Search } from "lucide-react";
import { useServerSearch } from "./use-server-search";

/**
 * A search box that adds an item: type, and the matches come from the server — every eligible item, not the first few
 * hundred a page could load. Items already chosen are left out of the matches.
 */
export function ItemSearch<T>(props: {
  source: (query: string) => string;
  idOf: (item: T) => string;
  render: (item: T) => ReactNode;
  onPick: (item: T) => void;
  exclude?: ReadonlySet<string>;
  placeholder?: string;
  label: string;
  disabled?: boolean;
}) {
  const { source, idOf, render, onPick, exclude, placeholder = "Search by title, brand, barcode or location", label, disabled } = props;
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const root = useRef<HTMLDivElement>(null);
  const listId = useId();
  const { results, loading, failed } = useServerSearch<T>(open ? source(query.trim()) : null);
  const matches = results.filter((item) => !exclude?.has(idOf(item)));

  useEffect(() => setActive(0), [results]);

  useEffect(() => {
    if (!open) return;
    const onPointer = (event: PointerEvent) => {
      if (root.current && !root.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", onPointer);
    return () => document.removeEventListener("pointerdown", onPointer);
  }, [open]);

  const pick = (item: T) => {
    onPick(item);
    setQuery("");
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setOpen(true);
      setActive((index) => Math.min(index + 1, matches.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActive((index) => Math.max(index - 1, 0));
    } else if (event.key === "Enter") {
      // Enter chooses a match; it must never submit the form around the picker.
      event.preventDefault();
      const item = matches[active];
      if (item) pick(item);
    } else if (event.key === "Escape") {
      setOpen(false);
    }
  };

  return (
    <div ref={root} className="relative">
      <label className="border-input bg-background focus-within:ring-ring flex items-center gap-2 rounded-lg border px-3 focus-within:ring-2">
        <Search className="text-muted-foreground h-4 w-4 shrink-0" aria-hidden />
        <input
          role="combobox"
          aria-expanded={open}
          aria-controls={listId}
          aria-activedescendant={open && matches[active] ? `${listId}-${idOf(matches[active]!)}` : undefined}
          aria-label={label}
          placeholder={placeholder}
          disabled={disabled}
          value={query}
          onFocus={() => setOpen(true)}
          onChange={(event) => {
            setQuery(event.target.value);
            setOpen(true);
          }}
          onKeyDown={onKeyDown}
          className="h-10 w-full bg-transparent text-sm outline-none disabled:opacity-50"
        />
      </label>
      {open ? (
        <ul id={listId} role="listbox" className="bg-background absolute z-20 mt-1 max-h-80 w-full overflow-y-auto rounded-lg border py-1 shadow-md">
          {matches.map((item, index) => (
            <li
              key={idOf(item)}
              id={`${listId}-${idOf(item)}`}
              role="option"
              aria-selected={index === active}
              onPointerEnter={() => setActive(index)}
              onPointerDown={(event) => {
                event.preventDefault();
                pick(item);
              }}
              className={`cursor-pointer px-3 py-2 text-sm ${index === active ? "bg-muted" : ""}`}
            >
              {render(item)}
            </li>
          ))}
          {loading && matches.length === 0 ? <li className="text-muted-foreground px-3 py-2 text-xs">Searching…</li> : null}
          {failed ? <li className="px-3 py-2 text-xs text-red-700">Inventory could not be searched. Try again.</li> : null}
          {!loading && !failed && matches.length === 0 ? (
            <li className="text-muted-foreground px-3 py-2 text-xs">
              {query.trim() ? `Nothing that can be added here matches “${query.trim()}”.` : "Nothing left that can be added here."}
            </li>
          ) : null}
        </ul>
      ) : null}
    </div>
  );
}
