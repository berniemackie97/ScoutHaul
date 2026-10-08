"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Check, ChevronsUpDown, Search } from "lucide-react";

export type ComboOption = { value: string; label: string; group?: string | null };

/**
 * Searchable, category-scoped picker (eBay/Depop-style): type to filter, pick from the list, or keep a
 * custom value when nothing matches. Submits via a hidden input so it drops into any form by `name`.
 */
export function Combobox({
  name,
  value,
  onChange,
  options,
  placeholder = "Search…",
  disabled = false,
  allowCustom = true,
  emptyLabel = "No matches",
}: {
  name: string;
  value: string;
  onChange: (value: string) => void;
  options: ComboOption[];
  placeholder?: string;
  disabled?: boolean;
  allowCustom?: boolean;
  emptyLabel?: string;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function onDocClick(event: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
  }, []);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    const base = q ? options.filter((option) => option.label.toLowerCase().includes(q)) : options;
    return base.slice(0, 100);
  }, [options, query]);

  // The trigger names the choice the way the list does ("US Letter (S/M/L/XL)"), not by its stored value.
  const shown = options.find((option) => option.value === value)?.label ?? value;
  const showCustom = allowCustom && query.trim().length > 0 && !options.some((option) => option.label.toLowerCase() === query.trim().toLowerCase());
  const rows = showCustom ? [{ value: query.trim(), label: `Use “${query.trim()}”`, group: "custom" } as ComboOption, ...filtered] : filtered;

  function select(option: ComboOption) {
    onChange(option.value);
    setQuery("");
    setOpen(false);
  }

  return (
    <div ref={rootRef} className="relative">
      <input type="hidden" name={name} value={value} />
      <button
        type="button"
        disabled={disabled}
        onClick={() => { setOpen((prev) => !prev); setActive(0); }}
        className="border-input bg-background focus:ring-ring flex h-10 w-full items-center justify-between gap-2 rounded-lg border px-3 text-left text-sm focus:outline-none focus:ring-2 disabled:cursor-not-allowed disabled:opacity-50"
      >
        <span className={`truncate ${value ? "" : "text-muted-foreground"}`}>{shown || placeholder}</span>
        <ChevronsUpDown className="text-muted-foreground h-4 w-4 shrink-0" />
      </button>

      {open && (
        <div className="bg-popover absolute z-20 mt-1 w-full rounded-lg border shadow-md">
          <div className="flex items-center gap-2 border-b px-2">
            <Search className="text-muted-foreground h-4 w-4" />
            <input
              autoFocus
              value={query}
              onChange={(event) => { setQuery(event.target.value); setActive(0); }}
              onKeyDown={(event) => {
                if (event.key === "ArrowDown") { event.preventDefault(); setActive((index) => Math.min(index + 1, rows.length - 1)); }
                else if (event.key === "ArrowUp") { event.preventDefault(); setActive((index) => Math.max(index - 1, 0)); }
                else if (event.key === "Enter") { event.preventDefault(); if (rows[active]) select(rows[active]); }
                else if (event.key === "Escape") setOpen(false);
              }}
              placeholder={placeholder}
              className="h-9 w-full bg-transparent text-sm focus:outline-none"
            />
          </div>
          <ul className="max-h-60 overflow-auto py-1 text-sm">
            {value && (
              <li>
                <button type="button" onClick={() => select({ value: "", label: "" })} className="text-muted-foreground hover:bg-muted flex w-full px-3 py-1.5 text-left text-xs">
                  Clear selection
                </button>
              </li>
            )}
            {rows.length === 0 && <li className="text-muted-foreground px-3 py-2">{emptyLabel}</li>}
            {rows.map((option, index) => (
              <li key={`${option.group ?? ""}:${option.value}`}>
                <button
                  type="button"
                  onMouseEnter={() => setActive(index)}
                  onClick={() => select(option)}
                  className={`flex w-full items-center justify-between px-3 py-1.5 text-left ${index === active ? "bg-muted" : ""}`}
                >
                  <span>{option.label}</span>
                  {option.value === value && <Check className="h-4 w-4" />}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
