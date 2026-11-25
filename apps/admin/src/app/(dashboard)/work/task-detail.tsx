"use client";

import { useState, useTransition } from "react";
import { Check } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Button, Card, Notice } from "@overhaul/ui";
import { When } from "@/components/when";
import { safely } from "@/lib/safe-action";
import { setAsideTask, type WorkActionResult } from "./actions";
import { TaskActions } from "./task-actions";

type Task = components["schemas"]["WorkTaskDto"];
type WorkWait = components["schemas"]["WorkWait"];

const WAITS: ReadonlyArray<readonly [WorkWait, string]> = [
  ["Buyer", "Buyer"],
  ["Carrier", "Carrier"],
  ["Marketplace", "Marketplace"],
  ["Payout", "Payout"],
];

function dayFromNow(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  date.setHours(9, 0, 0, 0);
  return date.toISOString();
}

/** The open task: why it matters, where its sale stands, and the one thing to do — or set it aside with a reason. */
export function TaskDetail({ task, onDone }: { task: Task; onDone: (message: string) => void }) {
  const [mode, setMode] = useState<"none" | "snooze" | "wait">("none");
  const [note, setNote] = useState("");
  const [result, setResult] = useState<WorkActionResult | null>(null);
  const [pending, startTransition] = useTransition();
  const steps = task.steps ?? [];

  // A task that is finished or set aside leaves the list at once; a failure stays here to be read.
  const settle = (outcome: WorkActionResult) => (outcome.ok ? onDone(outcome.message) : setResult(outcome));
  const aside = (wait: WorkWait, until: string | null) =>
    startTransition(async () => settle(await safely(() => setAsideTask(task.key ?? "", task.title ?? "", wait, until, note))));

  return (
    <Card className="space-y-4 p-5">
      <div>
        <p className="text-muted-foreground text-xs font-medium uppercase tracking-wide">{task.kind}</p>
        <h3 className="mt-1 text-lg font-semibold leading-snug">{task.title}</h3>
        <p className="mt-1 text-sm">{task.why}</p>
        {task.dueBy && (
          <p className={`mt-1 text-xs ${task.isLate ? "text-destructive font-semibold" : "text-muted-foreground"}`}>
            {task.isLate ? "Past due since" : "Due"} <When value={task.dueBy} />
          </p>
        )}
      </div>

      {steps.length > 0 && (
        <ol aria-label="Thread" className="space-y-0">
          {steps.map((step) => (
            <li key={step.label} className="flex items-start gap-3 py-1.5 text-sm">
              {step.state === "Done" ? (
                <span className="bg-success text-success-foreground mt-0.5 flex h-[18px] w-[18px] shrink-0 items-center justify-center rounded-full">
                  <Check className="h-3 w-3" aria-hidden />
                </span>
              ) : step.state === "Now" ? (
                <span className="border-copper bg-copper/10 mt-0.5 h-[18px] w-[18px] shrink-0 rounded-full border-2" />
              ) : (
                <span className="border-border mt-0.5 h-[18px] w-[18px] shrink-0 rounded-full border-[1.5px]" />
              )}
              <span className={step.state === "Now" ? "font-semibold" : step.state === "Later" ? "text-muted-foreground" : undefined}>
                {step.label}
                {step.at && (
                  <span className="text-muted-foreground block text-xs font-normal">
                    <When value={step.at} />
                  </span>
                )}
              </span>
            </li>
          ))}
        </ol>
      )}

      <div className="flex flex-wrap items-center gap-2 border-t pt-4">
        <TaskActions task={task} onResult={settle} />
        <Button type="button" variant="outline" size="sm" aria-expanded={mode === "snooze"} onClick={() => setMode(mode === "snooze" ? "none" : "snooze")}>
          Snooze…
        </Button>
        <Button type="button" variant="outline" size="sm" aria-expanded={mode === "wait"} onClick={() => setMode(mode === "wait" ? "none" : "wait")}>
          Waiting on…
        </Button>
      </div>

      {mode !== "none" && (
        <div className="bg-muted/40 space-y-3 rounded-md border p-3">
          <input
            value={note}
            onChange={(event) => setNote(event.target.value)}
            aria-label="Note"
            placeholder={mode === "snooze" ? "Why it can wait (optional)" : "What you are waiting for (optional)"}
            className="border-input bg-background focus-visible:ring-ring h-9 w-full rounded-md border px-3 text-sm focus-visible:outline-none focus-visible:ring-2"
          />
          <div className="flex flex-wrap gap-2">
            {mode === "snooze"
              ? ([["Tomorrow", 1], ["In 3 days", 3], ["Next week", 7]] as const).map(([label, days]) => (
                  <Button key={label} type="button" size="sm" variant="secondary" disabled={pending} onClick={() => aside("Snoozed", dayFromNow(days))}>
                    {label}
                  </Button>
                ))
              : WAITS.map(([wait, label]) => (
                  <Button key={wait} type="button" size="sm" variant="secondary" disabled={pending} onClick={() => aside(wait, dayFromNow(7))}>
                    {label}
                  </Button>
                ))}
          </div>
          {mode === "wait" && <p className="text-muted-foreground text-xs">A wait comes back in a week if nothing moves, so nothing stalls unseen.</p>}
        </div>
      )}

      {result && <Notice tone={result.ok ? "success" : "destructive"}>{result.message}</Notice>}
    </Card>
  );
}
