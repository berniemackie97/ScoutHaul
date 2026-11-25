"use client";

import { useTransition } from "react";
import type { components } from "@overhaul/api-client";
import { When } from "@/components/when";
import { safely } from "@/lib/safe-action";
import { bringBackTask } from "./actions";

type Wait = components["schemas"]["WorkWaitDto"];

/** Tasks set aside, by what they wait on. Each can be brought back to its lane early. */
export function WaitingList({ waiting }: { waiting: Wait[] }) {
  const [pending, startTransition] = useTransition();
  if (waiting.length === 0) return null;

  return (
    <div className="bg-card mt-4 space-y-2 rounded-lg border p-3 text-sm">
      <p className="font-semibold">Waiting, not stuck</p>
      <ul className="space-y-2">
        {waiting.map((entry) => (
          <li key={entry.key} className="space-y-0.5">
            <span className="block truncate">{entry.title ?? entry.key}</span>
            <span className="text-muted-foreground flex items-center gap-2 text-xs">
              <span>{entry.wait === "Snoozed" ? "Snoozed" : `On ${entry.wait?.toLowerCase()}`}</span>
              {entry.until && (
                <span>
                  · until <When value={entry.until} />
                </span>
              )}
              <button
                type="button"
                disabled={pending}
                onClick={() => startTransition(async () => void (await safely(() => bringBackTask(entry.key ?? ""))))}
                className="text-copper ml-auto font-medium hover:underline"
              >
                Bring back
              </button>
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
