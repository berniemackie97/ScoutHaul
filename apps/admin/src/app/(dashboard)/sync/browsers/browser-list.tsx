"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Badge, Button, Card, Input } from "@overhaul/ui";
import { ago, type SyncAgent } from "@/features/sync/sync-model";
import { disconnectBrowser, renameBrowser, type SyncActionResult } from "../actions";

const DAY = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric" });

export function BrowserList({ agents }: { agents: SyncAgent[] }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [message, setMessage] = useState<SyncActionResult | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [name, setName] = useState("");
  const now = new Date();

  const live = agents.filter((agent) => !agent.revoked);
  const gone = agents.filter((agent) => agent.revoked);
  const run = (action: () => Promise<SyncActionResult>) =>
    startTransition(async () => {
      const outcome = await action();
      setMessage(outcome);
      if (outcome.ok) {
        setEditing(null);
        router.refresh();
      }
    });

  return (
    <section aria-labelledby="browsers-heading" className="space-y-3">
      <h2 id="browsers-heading" className="text-sm font-semibold">Paired browsers</h2>
      <div aria-live="polite">
        {message ? <p className={`text-sm ${message.ok ? "text-success" : "text-destructive"}`}>{message.message}</p> : null}
      </div>

      {live.length === 0 ? (
        <Card className="text-muted-foreground p-6 text-sm">No browsers are paired yet.</Card>
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          {live.map((agent) => (
            <Card key={agent.id} className="space-y-3 p-5">
              <div className="flex items-start justify-between gap-3">
                {editing === agent.id ? (
                  <form
                    className="flex flex-1 gap-2"
                    onSubmit={(event) => {
                      event.preventDefault();
                      run(() => renameBrowser(agent.id!, name));
                    }}
                  >
                    <Input value={name} onChange={(event) => setName(event.target.value)} maxLength={80} aria-label="Browser name" autoFocus />
                    <Button size="sm" type="submit" disabled={pending || !name.trim()}>Save</Button>
                    <Button size="sm" variant="ghost" type="button" onClick={() => setEditing(null)}>Cancel</Button>
                  </form>
                ) : (
                  <div>
                    <p className="font-semibold">{agent.name}</p>
                    <p className="text-muted-foreground text-xs">
                      {[agent.browser, agent.operatingSystem, agent.extensionVersion ? `v${agent.extensionVersion}` : null].filter(Boolean).join(" · ")}
                    </p>
                  </div>
                )}
                <Badge variant={agent.online ? "success" : "secondary"}>{agent.online ? "Online" : `Seen ${ago(agent.lastSeenAt, now)}`}</Badge>
              </div>

              <ul className="space-y-1 text-sm">
                {(agent.marketplaces ?? []).length === 0 ? (
                  <li className="text-muted-foreground">Has not reported any marketplace yet.</li>
                ) : (
                  (agent.marketplaces ?? []).map((marketplace) => (
                    <li key={marketplace.platform} className="flex justify-between gap-3">
                      <span className="font-medium">{marketplace.platform}</span>
                      <span className="text-muted-foreground text-right text-xs">
                        {!marketplace.accessGranted
                          ? "Not allowed to reach it yet"
                          : marketplace.pausedUntil
                            ? `Paused: ${marketplace.pauseReason ?? ""}`
                            : marketplace.signedIn
                              ? `Signed in as ${marketplace.username}`
                              : marketplace.signedIn === false
                                ? "Signed out"
                                : "Not checked yet"}
                      </span>
                    </li>
                  ))
                )}
              </ul>

              <div className="text-muted-foreground flex flex-wrap items-center justify-between gap-2 text-xs">
                <span>
                  Paired by {agent.pairedBy} on {agent.pairedAt ? DAY.format(new Date(agent.pairedAt)) : "—"}
                </span>
                <span className="flex gap-1">
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={pending}
                    onClick={() => {
                      setEditing(agent.id!);
                      setName(agent.name ?? "");
                    }}
                  >
                    Rename
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="text-destructive"
                    disabled={pending}
                    onClick={() => {
                      if (window.confirm(`Disconnect ${agent.name}? Its token stops working immediately, and anything it is running stops.`)) {
                        run(() => disconnectBrowser(agent.id!));
                      }
                    }}
                  >
                    Disconnect
                  </Button>
                </span>
              </div>
            </Card>
          ))}
        </div>
      )}

      {gone.length > 0 ? (
        <details className="text-muted-foreground text-sm">
          <summary className="cursor-pointer">{gone.length} disconnected browser{gone.length === 1 ? "" : "s"}</summary>
          <ul className="mt-2 space-y-1">
            {gone.map((agent) => (
              <li key={agent.id}>
                {agent.name} · paired by {agent.pairedBy}
              </li>
            ))}
          </ul>
        </details>
      ) : null}
    </section>
  );
}
