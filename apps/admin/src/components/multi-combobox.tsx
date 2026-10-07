"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Check, Plus, Search, X } from "lucide-react";
import { writeListInput } from "@/lib/list-input";
import type { ComboOption } from "./combobox";

/**
 * Several picks from a closed list, up to `max`: chosen values show as chips, the rest are searched and toggled in a
 * list. Submits one hidden input holding the values; read it back with `readListInput`.
 */
export function MultiCombobox({
  name,
  values,
  onChange,
  options,
  max,
  placeholder = "Add…",
  disabled = false,
}: {
  name: string;
  values: readonly string[];
  onChange: (values: string[]) => void;
  options: ComboOption[];
  max: number;
  placeholder?: string;
  disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const full = values.length >= max;

  useEffect(() => {
    function onDocClick(event: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
  }, []);

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase();
    return (q ? options.filter((option) => option.label.toLowerCase().includes(q)) : options).slice(0, 100);
  }, [options, query]);

  const labelOf = (value: string) => options.find((option) => option.value === value)?.label ?? value;

  function toggle(value: string) {
    if (values.includes(value)) onChange(values.filter((entry) => entry !== value));
    else if (!full) onChange([...values, value]);
    setQuery("");
  }

  return (
    <div ref={rootRef} className="relative">
      <input type="hidden" name={name} value={writeListInput(values)} />
      <div
        className={`border-input bg-background flex min-h-10 w-full flex-wrap items-center gap-1.5 rounded-lg border px-2 py-1.5 text-sm ${disabled ? "cursor-not-allowed opacity-50" : ""}`}
      >
        {values.map((value) => (
          <span key={value} className="bg-muted inline-flex items-center gap-1 rounded-md py-0.5 pl-2 pr-1 text-xs font-medium">
            {labelOf(value)}
            <button
              type="button"
              disabled={disabled}
              onClick={() => toggle(value)}
              aria-label={`Remove ${labelOf(value)}`}
              className="text-muted-foreground hover:text-foreground rounded p-0.5"
            >
              <X className="h-3 w-3" />
            </button>
          </span>
        ))}
        {full ? (
          <span className="text-muted-foreground px-1 text-xs">Up to {max}</span>
        ) : (
          <button
            type="button"
            disabled={disabled}
            onClick={() => {
              setOpen((prev) => !prev);
              setActive(0);
            }}
            aria-expanded={open}
            className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 rounded px-1 text-sm"
          >
            {values.length === 0 ? placeholder : <Plus className="h-4 w-4" aria-label="Add another" />}
          </button>
        )}
      </div>

      {open && !full && (
        <div className="bg-popover absolute z-20 mt-1 w-full rounded-lg border shadow-md">
          <div className="flex items-center gap-2 border-b px-2">
            <Search className="text-muted-foreground h-4 w-4" />
            <input
              autoFocus
              value={query}
              onChange={(event) => {
                setQuery(event.target.value);
                setActive(0);
              }}
              onKeyDown={(event) => {
                if (event.key === "ArrowDown") {
                  event.preventDefault();
                  setActive((index) => Math.min(index + 1, rows.length - 1));
                } else if (event.key === "ArrowUp") {
                  event.preventDefault();
                  setActive((index) => Math.max(index - 1, 0));
                } else if (event.key === "Enter") {
                  event.preventDefault();
                  if (rows[active]) toggle(rows[active].value);
                } else if (event.key === "Escape") setOpen(false);
              }}
              placeholder="Search…"
              className="h-9 w-full bg-transparent text-sm focus:outline-none"
            />
          </div>
          <ul className="max-h-60 overflow-auto py-1 text-sm">
            {rows.length === 0 && <li className="text-muted-foreground px-3 py-2">No matches</li>}
            {rows.map((option, index) => {
              const chosen = values.includes(option.value);
              return (
                <li key={option.value}>
                  <button
                    type="button"
                    onMouseEnter={() => setActive(index)}
                    onClick={() => toggle(option.value)}
                    className={`flex w-full items-center justify-between px-3 py-1.5 text-left ${index === active ? "bg-muted" : ""}`}
                  >
                    <span>{option.label}</span>
                    {chosen && <Check className="h-4 w-4" />}
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
