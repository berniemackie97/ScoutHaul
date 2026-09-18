"use client";

import { useSyncExternalStore } from "react";
import { Search } from "lucide-react";
import { cn } from "@overhaul/ui";

/**
 * Whether this is a Mac, for the shortcut hint. Read once on the client; the server renders no hint,
 * so the label cannot disagree with itself across hydration.
 */
function useIsMac(): boolean | null {
  return useSyncExternalStore(
    () => () => {},
    () => /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent),
    () => null,
  );
}

/**
 * The visible way into the palette.
 *
 * A keyboard shortcut nobody has been told about is a feature nobody uses, so the palette also has a
 * box on screen that looks like what it is — a search — and says which keys open it.
 */
export function CommandPaletteTrigger({
  onOpen,
  compact = false,
  className,
}: {
  onOpen: () => void;
  /** Icon only, for the collapsed sidebar rail. */
  compact?: boolean;
  className?: string;
}) {
  const isMac = useIsMac();

  if (compact) {
    return (
      <button
        type="button"
        onClick={onOpen}
        aria-label="Search and commands"
        title={isMac === null ? "Search" : `Search (${isMac ? "⌘" : "Ctrl+"}K)`}
        className={cn("hover:bg-accent text-muted-foreground flex h-9 w-9 items-center justify-center rounded-lg", className)}
      >
        <Search className="h-4 w-4" />
      </button>
    );
  }

  return (
    <button
      type="button"
      onClick={onOpen}
      aria-label="Search and commands"
      className={cn(
        "bg-background hover:bg-accent text-muted-foreground flex h-9 w-full items-center gap-2 rounded-lg border px-3 text-left text-sm transition-colors",
        className,
      )}
    >
      <Search className="h-4 w-4 shrink-0" aria-hidden />
      <span className="flex-1 truncate">Search or jump to…</span>
      {isMac === null ? null : (
        <kbd className="bg-muted shrink-0 rounded border px-1.5 font-sans text-[10px]">{isMac ? "⌘K" : "Ctrl K"}</kbd>
      )}
    </button>
  );
}
