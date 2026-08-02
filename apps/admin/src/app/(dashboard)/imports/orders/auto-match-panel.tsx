"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Sparkles } from "lucide-react";
import { Button } from "@overhaul/ui";
import { autoMatchWaitingOrders } from "./actions";

/**
 * Offers to settle the waiting orders that leave nothing to decide.
 *
 * The count is a preview: nothing is written until the operator says so. Bulk-creating sales against
 * records imported weeks ago should never be a side effect of opening a page, so this is an explicit
 * action with the number stated up front.
 */
export function AutoMatchPanel({ canSettle }: { canSettle: number }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [message, setMessage] = useState<string | null>(null);

  if (canSettle === 0 && !message) return null;

  return (
    <div className="flex flex-col gap-3 rounded-xl border border-brand-600/30 bg-brand-50/40 p-4 sm:flex-row sm:items-center sm:justify-between dark:bg-brand-950/20">
      <div className="flex items-start gap-3">
        <Sparkles className="text-brand-600 mt-0.5 h-5 w-5 shrink-0" aria-hidden />
        <div>
          <p className="text-sm font-medium">
            {canSettle} order{canSettle === 1 ? "" : "s"} need no decision
          </p>
          <p className="text-muted-foreground text-xs">
            Every line matches an inventory item exactly — by the platform&rsquo;s item number or its
            code. Recording them writes each sale with the platform&rsquo;s own figures. Anything
            matched only by title is left below for you.
          </p>
          {message && <p className="mt-2 text-xs font-medium">{message}</p>}
        </div>
      </div>
      {canSettle > 0 && (
        <Button
          type="button"
          disabled={pending}
          className="shrink-0"
          onClick={() =>
            startTransition(async () => {
              const result = await autoMatchWaitingOrders();
              setMessage(result.message);
              if (result.ok) router.refresh();
            })
          }
        >
          {pending ? "Recording…" : `Record ${canSettle}`}
        </Button>
      )}
    </div>
  );
}
