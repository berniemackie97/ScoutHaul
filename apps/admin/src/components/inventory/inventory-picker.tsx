"use client";

import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { Check, ChevronDown, PackagePlus, Search, TriangleAlert } from "lucide-react";
import { formatCurrency } from "@overhaul/ui";
import type { LineChoice } from "@/features/imports/reconcile-model";
import type { PickerItem } from "@/app/api/inventory/picker/route";
import { useServerSearch } from "./use-server-search";

export type PickerSuggestion = { id: string; title: string; alreadySold: boolean; exact: boolean };

type Option =
  | { key: string; kind: "item"; item: { id: string; title: string; isSold: boolean; status?: string | null; code?: string | null; price?: number | null; thumbnailUrl?: string | null }; suggested?: PickerSuggestion }
  | { key: string; kind: "create" };

/**
 * Chooses the inventory item an order line is — searched on the server as you type, suggestion first
 * with the reason it was suggested, and "create a new item from this sale" for things that sold
 * before they were ever recorded. Opens in place, so it is never clipped by the table around it.
 */
export function InventoryPicker(props: {
  value: LineChoice | null;
  suggestion: PickerSuggestion | null;
  lineTitle: string;
  disabled?: boolean;
  onChange: (choice: LineChoice | null) => void;
}) {
  const { value, suggestion, lineTitle, disabled, onChange } = props;
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const root = useRef<HTMLDivElement>(null);
  const listId = useId();
  const { results, loading, failed } = useServerSearch<PickerItem>(open ? `/api/inventory/picker?q=${encodeURIComponent(query)}` : null);

  useEffect(() => setActive(0), [results]);

  // Close when the operator clicks anywhere else.
  useEffect(() => {
    if (!open) return;
    const onPointer = (event: PointerEvent) => {
      if (root.current && !root.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", onPointer);
    return () => document.removeEventListener("pointerdown", onPointer);
  }, [open]);

  const options = useMemo<Option[]>(() => {
    const list: Option[] = [];
    if (suggestion) {
      list.push({ key: `s-${suggestion.id}`, kind: "item", item: { id: suggestion.id, title: suggestion.title, isSold: suggestion.alreadySold }, suggested: suggestion });
    }

    for (const item of results) {
      if (item.id !== suggestion?.id) list.push({ key: item.id, kind: "item", item });
    }

    list.push({ key: "create", kind: "create" });
    return list;
  }, [results, suggestion]);

  const choose = (option: Option) => {
    onChange(option.kind === "create" ? { kind: "create" } : { kind: "item", id: option.item.id, title: option.item.title, isSold: option.item.isSold });
    setOpen(false);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActive((index) => Math.min(index + 1, options.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActive((index) => Math.max(index - 1, 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      const option = options[active];
      if (option) choose(option);
    } else if (event.key === "Escape") {
      setOpen(false);
    }
  };

  return (
    <div ref={root} className="w-full min-w-[16rem]">
      <button
        type="button"
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={() => {
          setOpen((current) => !current);
          setQuery("");
        }}
        className="border-input bg-background hover:border-foreground/30 flex h-auto min-h-9 w-full items-center justify-between gap-2 rounded-lg border px-3 py-1.5 text-left text-sm disabled:opacity-50"
      >
        <span className="min-w-0">
          {value?.kind === "create" ? (
            <span className="flex items-center gap-1.5 font-medium text-success">
              <PackagePlus className="h-4 w-4 shrink-0" aria-hidden /> New item from this sale
            </span>
          ) : value?.kind === "item" ? (
            <span className="block truncate">
              {value.title}
              {value.isSold ? <span className="text-warning"> · recorded</span> : null}
            </span>
          ) : (
            <span className="text-muted-foreground">Choose an item…</span>
          )}
        </span>
        <ChevronDown className="text-muted-foreground h-4 w-4 shrink-0" aria-hidden />
      </button>

      {open ? (
        <div className="bg-background mt-1 rounded-lg border shadow-sm">
          <label className="flex items-center gap-2 border-b px-3">
            <Search className="text-muted-foreground h-4 w-4 shrink-0" aria-hidden />
            <input
              autoFocus
              role="combobox"
              aria-expanded
              aria-controls={listId}
              aria-activedescendant={options[active] ? `${listId}-${options[active]!.key}` : undefined}
              aria-label={`Search inventory for "${lineTitle}"`}
              placeholder="Search by title, SKU or barcode"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              onKeyDown={onKeyDown}
              className="h-9 w-full bg-transparent text-sm outline-none"
            />
          </label>
          <ul id={listId} role="listbox" className="max-h-72 overflow-y-auto py-1">
            {options.map((option, index) => (
              <li
                key={option.key}
                id={`${listId}-${option.key}`}
                role="option"
                aria-selected={index === active}
                onPointerEnter={() => setActive(index)}
                onPointerDown={(event) => {
                  event.preventDefault();
                  choose(option);
                }}
                className={`cursor-pointer px-3 py-2 text-sm ${index === active ? "bg-muted" : ""} ${option.kind === "create" ? "border-t" : ""}`}
              >
                {option.kind === "create" ? (
                  <span className="flex items-start gap-2">
                    <PackagePlus className="mt-0.5 h-4 w-4 shrink-0 text-success" aria-hidden />
                    <span>
                      <span className="font-medium">Create a new item from this sale</span>
                      <span className="text-muted-foreground block text-xs">For something that sold before it was recorded. Its cost is left for you to add.</span>
                    </span>
                  </span>
                ) : (
                  <ItemRow option={option} selected={value?.kind === "item" && value.id === option.item.id} />
                )}
              </li>
            ))}
            {loading && results.length === 0 ? <li className="text-muted-foreground px-3 py-2 text-xs">Searching…</li> : null}
            {failed ? <li className="px-3 py-2 text-xs text-destructive">Inventory could not be searched. Try again.</li> : null}
            {!loading && !failed && query && results.length === 0 ? (
              <li className="text-muted-foreground px-3 py-2 text-xs">Nothing in inventory matches “{query}”.</li>
            ) : null}
          </ul>
          {value ? (
            <button type="button" className="text-muted-foreground hover:text-foreground w-full border-t px-3 py-2 text-left text-xs" onClick={() => { onChange(null); setOpen(false); }}>
              Clear the choice
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

function ItemRow({ option, selected }: { option: Extract<Option, { kind: "item" }>; selected: boolean }) {
  const { item, suggested } = option;
  const details = [item.status, item.code, typeof item.price === "number" ? formatCurrency(item.price) : null].filter(Boolean).join(" · ");
  return (
    <span className="flex items-start gap-2">
      {item.thumbnailUrl ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={item.thumbnailUrl} alt="" className="h-9 w-9 shrink-0 rounded object-cover" />
      ) : (
        <span className="bg-muted h-9 w-9 shrink-0 rounded" aria-hidden />
      )}
      <span className="min-w-0 flex-1">
        <span className="flex items-center gap-1.5">
          <span className="truncate font-medium">{item.title}</span>
          {selected ? <Check className="h-4 w-4 shrink-0 text-success" aria-hidden /> : null}
        </span>
        {suggested ? (
          suggested.exact ? (
            <span className="block text-xs text-success">Suggested — the item number or SKU matches</span>
          ) : (
            <span className="flex items-center gap-1 text-xs text-warning">
              <TriangleAlert className="h-3 w-3" aria-hidden /> Suggested only because the title looks similar — check it
            </span>
          )
        ) : null}
        {item.isSold ? (
          <span className="block text-xs text-warning">Already recorded as sold — choosing it corrects that sale</span>
        ) : details ? (
          <span className="text-muted-foreground block truncate text-xs">{details}</span>
        ) : null}
      </span>
    </span>
  );
}
