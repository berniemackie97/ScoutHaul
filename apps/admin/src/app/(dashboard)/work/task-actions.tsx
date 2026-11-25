"use client";

import Link from "next/link";
import { useTransition } from "react";
import { ExternalLink } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Button } from "@overhaul/ui";
import { safely } from "@/lib/safe-action";
import { clearSoldListing, confirmDelisted, keepListing, type WorkActionResult } from "./actions";

type Task = components["schemas"]["WorkTaskDto"];

/**
 * The task's own actions, done here. A delist opens the marketplace listing in a new tab and is confirmed in place; a
 * sold listing opens the item with its sale form filled. Ending a listing stays a person's click on the marketplace —
 * HaulScout records it, it does not do it.
 */
export function TaskActions({ task, onResult }: { task: Task; onResult: (result: WorkActionResult) => void }) {
  const [pending, startTransition] = useTransition();
  const run = (action: () => Promise<WorkActionResult>) => startTransition(async () => onResult(await safely(action)));
  const id = task.entityId ?? "";

  if (task.type === "delist") {
    return (
      <>
        {task.externalUrl ? (
          <Button asChild variant="outline">
            <a href={task.externalUrl} target="_blank" rel="noreferrer">
              <ExternalLink className="mr-2 h-4 w-4" aria-hidden /> Open on {task.platform}
            </a>
          </Button>
        ) : (
          <Button asChild variant="outline">
            <Link href={task.actionHref ?? "/"}>Open item</Link>
          </Button>
        )}
        <Button type="button" disabled={pending} onClick={() => run(() => confirmDelisted(id))}>
          I&rsquo;ve taken it down
        </Button>
        <Button type="button" variant="ghost" disabled={pending} onClick={() => run(() => keepListing(id))}>
          Leave it listed
        </Button>
      </>
    );
  }

  if (task.type === "sold") {
    return (
      <>
        <Button asChild>
          <Link href={task.actionHref ?? "/"}>Record sale</Link>
        </Button>
        {task.externalUrl && (
          <Button asChild variant="outline">
            <a href={task.externalUrl} target="_blank" rel="noreferrer">
              <ExternalLink className="mr-2 h-4 w-4" aria-hidden /> Open listing
            </a>
          </Button>
        )}
        <Button type="button" variant="ghost" disabled={pending} onClick={() => run(() => clearSoldListing(id))}>
          No sale — clear
        </Button>
      </>
    );
  }

  return (
    <Button asChild>
      <Link href={task.actionHref ?? "/"}>{task.actionLabel}</Link>
    </Button>
  );
}
