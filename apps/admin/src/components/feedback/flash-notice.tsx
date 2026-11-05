"use client";

import { useEffect, useState, type ReactNode } from "react";
import { CheckCircle2, X } from "lucide-react";

/**
 * The confirmation shown after a redirect, driven by a `?notice=` the action added. The parameter is dropped from the
 * address at once, so a reload or a shared link does not announce the same thing again.
 */
export function FlashNotice({ title, children }: { title: string; children?: ReactNode }) {
  const [open, setOpen] = useState(true);

  useEffect(() => {
    const url = new URL(window.location.href);
    if (!url.searchParams.has("notice")) return;
    url.searchParams.delete("notice");
    window.history.replaceState(window.history.state, "", url.toString());
  }, []);

  if (!open) return null;

  return (
    <div
      role="status"
      className="flex items-start gap-3 rounded-xl border border-green-300 bg-green-50 px-4 py-3 text-sm text-green-900 shadow-sm dark:border-green-800 dark:bg-green-950/40 dark:text-green-100 print:hidden"
    >
      <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0" aria-hidden />
      <div className="flex-1 space-y-1">
        <p className="font-semibold">{title}</p>
        {children ? <div className="text-green-800 dark:text-green-200">{children}</div> : null}
      </div>
      <button type="button" onClick={() => setOpen(false)} className="rounded p-1 opacity-70 hover:opacity-100" aria-label="Dismiss">
        <X className="h-4 w-4" aria-hidden />
      </button>
    </div>
  );
}
