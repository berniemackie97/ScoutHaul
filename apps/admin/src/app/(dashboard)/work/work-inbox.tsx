"use client";

import { useEffect, useMemo, useState } from "react";
import type { components } from "@overhaul/api-client";
import { Card } from "@overhaul/ui";
import { When } from "@/components/when";
import { TaskDetail } from "./task-detail";
import { WaitingList } from "./waiting-list";

type Inbox = components["schemas"]["WorkInboxDto"];
type Task = components["schemas"]["WorkTaskDto"];

/**
 * Lanes on the left, the lane's tasks in the middle, the open task's thread on the right. J and K move through the
 * lane, Enter opens the task's action — the keyboard path for a worker clearing a queue.
 */
export function WorkInbox({ inbox, initialLane }: { inbox: Inbox; initialLane?: string }) {
  const lanes = inbox.lanes ?? [];
  const firstBusy = lanes.find((lane) => (lane.tasks?.length ?? 0) > 0)?.key ?? lanes[0]?.key ?? "";
  const [laneKey, setLaneKey] = useState(lanes.some((lane) => lane.key === initialLane) ? initialLane! : firstBusy);
  const lane = lanes.find((entry) => entry.key === laneKey) ?? lanes[0];
  // Finished here, so gone from the list before the server's refresh lands.
  const [done, setDone] = useState<Set<string>>(new Set());
  const [toast, setToast] = useState<string | null>(null);
  const tasks = useMemo(() => (lane?.tasks ?? []).filter((task) => !done.has(task.key ?? "")), [lane, done]);
  const [selectedKey, setSelectedKey] = useState<string | null>(tasks[0]?.key ?? null);
  const selected: Task | undefined = tasks.find((task) => task.key === selectedKey) ?? tasks[0];

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      const target = event.target as HTMLElement | null;
      if (target && (target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName))) return;
      if (event.metaKey || event.ctrlKey || event.altKey) return;
      const index = tasks.findIndex((task) => task.key === selected?.key);
      if (event.key === "j" || event.key === "ArrowDown") {
        const next = tasks[Math.min(tasks.length - 1, index + 1)];
        if (next) setSelectedKey(next.key ?? null);
      } else if (event.key === "k" || event.key === "ArrowUp") {
        const previous = tasks[Math.max(0, index - 1)];
        if (previous) setSelectedKey(previous.key ?? null);
      } else if (event.key === "Enter" && selected?.actionHref && selected.type !== "delist") {
        window.location.assign(selected.actionHref);
      } else {
        return;
      }
      event.preventDefault();
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [tasks, selected]);

  return (
    <div className="space-y-4">
      <div className="text-muted-foreground flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
        <span>
          <span className="text-foreground font-semibold tabular-nums">{inbox.openCount}</span> open
        </span>
        <span className={inbox.lateCount ? "text-destructive font-medium" : undefined}>
          <span className="tabular-nums">{inbox.lateCount}</span> past due
        </span>
        <span>
          <span className="tabular-nums">{inbox.inTransit}</span> with a carrier
        </span>
        <span className="ml-auto text-xs">
          J / K to move · Enter to act · updated <When value={inbox.generatedAt} />
        </span>
      </div>

      <div className="grid gap-4 lg:grid-cols-[220px_minmax(0,1fr)_minmax(0,1.1fr)]">
        <nav aria-label="Lanes" className="space-y-1">
          {lanes.map((entry) => {
            const active = entry.key === lane?.key;
            const remaining = (entry.tasks ?? []).filter((task) => !done.has(task.key ?? ""));
            const count = remaining.reduce((total, task) => total + (task.count ?? 1), 0);
            const late = remaining.some((task) => task.isLate);
            return (
              <button
                key={entry.key}
                type="button"
                aria-current={active ? "page" : undefined}
                onClick={() => {
                  setLaneKey(entry.key ?? "");
                  setSelectedKey(entry.tasks?.[0]?.key ?? null);
                }}
                className={`flex min-h-11 w-full items-center gap-2 rounded-md px-3 text-left text-sm transition-colors ${
                  active ? "bg-primary text-primary-foreground font-semibold" : "hover:bg-muted"
                }`}
              >
                <span className="flex-1">{entry.label}</span>
                {late && <span className="bg-destructive h-1.5 w-1.5 rounded-full" aria-label="Has late tasks" />}
                <span className={`rounded-full px-2 text-xs tabular-nums ${active ? "bg-primary-foreground/15" : "bg-muted"}`}>{count}</span>
              </button>
            );
          })}
          <WaitingList waiting={inbox.waiting ?? []} />
        </nav>

        <section aria-label={lane?.label ?? "Tasks"} className="min-w-0 space-y-2">
          <div className="flex items-baseline gap-2 px-1">
            <h2 className="text-base font-semibold">{lane?.label}</h2>
            <span className="text-muted-foreground text-sm">{lane?.hint}</span>
          </div>
          {tasks.length === 0 ? (
            <Card className="text-muted-foreground border-dashed p-8 text-center text-sm">Lane clear. New work lands here as it happens.</Card>
          ) : (
            <ul className="space-y-2">
              {tasks.map((task) => {
                const active = task.key === selected?.key;
                return (
                  <li key={task.key}>
                    <button
                      type="button"
                      aria-current={active}
                      onClick={() => setSelectedKey(task.key ?? null)}
                      className={`bg-card flex w-full items-start gap-3 rounded-lg border p-3 text-left transition-colors ${
                        active ? "border-copper ring-copper/25 ring-2" : "hover:bg-muted/40"
                      }`}
                    >
                      <span className="min-w-0 flex-1">
                        <span className={`block truncate ${active ? "font-semibold" : "font-medium"}`}>{task.title}</span>
                        {task.detail && <span className="text-muted-foreground block truncate text-xs">{task.detail}</span>}
                      </span>
                      <span className={`shrink-0 text-xs tabular-nums ${task.isLate ? "text-destructive font-semibold" : "text-muted-foreground"}`}>
                        {task.count && task.count > 1 ? `×${task.count}` : task.since ? <When value={task.since} /> : null}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </section>

        <aside aria-label="Task" className="space-y-3">
          {toast && (
            <p role="status" className="bg-primary text-primary-foreground rounded-md px-3 py-2 text-sm">
              {toast}
            </p>
          )}
          {selected ? (
            <TaskDetail
              key={selected.key}
              task={selected}
              onDone={(message) => {
                const index = tasks.findIndex((task) => task.key === selected.key);
                const next = tasks[index + 1] ?? tasks[index - 1];
                setDone(new Set(done).add(selected.key ?? ""));
                setSelectedKey(next?.key ?? null);
                setToast(message);
              }}
            />
          ) : null}
        </aside>
      </div>
    </div>
  );
}
