"use client";

import { useEffect, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Check, Copy, MonitorSmartphone } from "lucide-react";
import { Button, Card } from "@overhaul/ui";
import { formatCode, type SyncAgent } from "@/features/sync/sync-model";
import { useSyncExtension } from "@/features/sync/use-sync-extension";
import { createPairing } from "../actions";

type Pairing = { pairingId: string; code: string; expiresAt: string };
type PairingState = { status?: "Waiting" | "Paired" | "Expired"; agent?: SyncAgent | null };

/**
 * Connecting a browser: a one-time code, shown here and typed or pasted into the extension. The
 * code is never handed to the extension by this page — only the person at that browser can
 * connect it, so nothing running on a page can pair a browser to the wrong organization.
 */
export function ConnectBrowser({ startOpen }: { startOpen: boolean }) {
  const router = useRouter();
  const { status: extension, recheck } = useSyncExtension();
  const [pairing, setPairing] = useState<Pairing | null>(null);
  const [state, setState] = useState<PairingState>({});
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [pending, startTransition] = useTransition();
  const [now, setNow] = useState(() => Date.now());

  const start = () =>
    startTransition(async () => {
      setError(null);
      setState({});
      const created = await createPairing();
      if (created.ok) {
        setPairing(created);
      } else {
        setError(created.message);
      }
    });

  useEffect(() => {
    if (startOpen) start();
    // Only on arrival from "Connect a browser".
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Watch for the extension to use the code, and count the code down while it is on screen.
  useEffect(() => {
    if (!pairing || state.status === "Paired" || state.status === "Expired") return;
    const tick = setInterval(() => setNow(Date.now()), 1000);
    const poll = setInterval(async () => {
      const response = await fetch(`/api/sync/pairings/${pairing.pairingId}`, { cache: "no-store" });
      if (!response.ok) return;
      const next = (await response.json()) as PairingState;
      setState(next);
      if (next.status === "Paired") {
        recheck();
        router.refresh();
      }
    }, 2000);
    return () => {
      clearInterval(tick);
      clearInterval(poll);
    };
  }, [pairing, state.status, recheck, router]);

  const secondsLeft = pairing ? Math.max(0, Math.round((Date.parse(pairing.expiresAt) - now) / 1000)) : 0;

  return (
    <Card className="p-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="max-w-xl">
          <h2 className="flex items-center gap-2 text-base font-semibold">
            <MonitorSmartphone className="h-4 w-4" aria-hidden /> Connect a browser
          </h2>
          <p className="text-muted-foreground mt-1 text-sm">
            Use the browser you sell in — the one signed in to your marketplaces. An always-on computer works best: it keeps syncing while your laptop sleeps.
          </p>
          {extension.installed && !extension.paired ? (
            <p className="mt-2 text-sm font-medium text-emerald-700">HaulScout Sync is installed in this browser — open it from the toolbar and paste the code.</p>
          ) : null}
        </div>
        {!pairing || state.status === "Expired" ? (
          <Button onClick={start} disabled={pending}>{pending ? "Creating code…" : pairing ? "New code" : "Get a code"}</Button>
        ) : null}
      </div>

      {error ? <p className="mt-4 text-sm text-red-700">{error}</p> : null}

      {pairing && state.status !== "Paired" && state.status !== "Expired" ? (
        <div className="mt-5 grid gap-5 sm:grid-cols-[auto_1fr] sm:items-center">
          <div className="flex items-center gap-3">
            <code className="bg-muted rounded-lg px-4 py-3 font-mono text-3xl font-semibold tracking-[0.2em]" aria-label="Pairing code">
              {formatCode(pairing.code)}
            </code>
            <Button
              variant="outline"
              size="icon"
              aria-label="Copy the code"
              onClick={async () => {
                await navigator.clipboard.writeText(formatCode(pairing.code));
                setCopied(true);
                setTimeout(() => setCopied(false), 1500);
              }}
            >
              {copied ? <Check className="h-4 w-4" aria-hidden /> : <Copy className="h-4 w-4" aria-hidden />}
            </Button>
          </div>
          <ol className="text-muted-foreground list-decimal space-y-1 pl-5 text-sm">
            <li>In that browser, open <strong className="text-foreground">HaulScout Sync</strong> from the toolbar.</li>
            <li>Paste this code, name the browser, and choose <strong className="text-foreground">Connect</strong>.</li>
            <li>Allow it to reach each marketplace when it asks.</li>
            <li className="list-none text-xs">
              Waiting for the browser… the code expires in {Math.floor(secondsLeft / 60)}:{String(secondsLeft % 60).padStart(2, "0")}.
            </li>
          </ol>
        </div>
      ) : null}

      {state.status === "Paired" ? (
        <p className="mt-4 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          Connected <strong>{state.agent?.name}</strong>. Next, bind each account on the Sync page to the seller account it is signed in to.
        </p>
      ) : null}
      {state.status === "Expired" ? <p className="mt-4 text-sm text-amber-700">That code expired before it was used. Get a new one.</p> : null}
    </Card>
  );
}
