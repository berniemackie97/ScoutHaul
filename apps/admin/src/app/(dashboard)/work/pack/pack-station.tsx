"use client";

import Link from "next/link";
import { useState, useTransition } from "react";
import { Check, ImageIcon, ScanLine } from "lucide-react";
import { Button, Card, Input, Notice, formatCurrency } from "@overhaul/ui";
import { advanceOrder } from "../../orders/[id]/actions";
import { safely } from "@/lib/safe-action";
import { scanMatches } from "./pack-scan";

export type PackLine = {
  lineId: string;
  itemId: string;
  title: string;
  sku: string | null;
  barcode: string | null;
  bin: string | null;
  condition: string | null;
  flaws: string | null;
  included: string | null;
  quantity: number;
  photoUrl: string | null;
};

type PackOrder = {
  id: string;
  number: string;
  channel: string;
  buyer: string | null;
  shipTo: string | null;
  note: string | null;
  localPickup: boolean;
  total: number;
};

const STEPS = ["Pick", "Check", "Pack", "Done"] as const;

/**
 * Packing one order, a step at a time. Pick confirms each item, by hand or by scanning its tag when there is a scanner;
 * Check walks what the listing promised; Pack records the order packed, which moves its thread to Ship.
 */
export function PackStation({ order, lines, run }: { order: PackOrder; lines: PackLine[]; run: { position: number; total: number; nextOrderId: string | null } }) {
  const [step, setStep] = useState(0);
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [scan, setScan] = useState("");
  const [scanError, setScanError] = useState<string | null>(null);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const [pending, startTransition] = useTransition();

  const checks = lines.flatMap((line) =>
    [
      line.condition && { key: `${line.lineId}:condition`, text: `${line.title}: condition as listed — ${line.condition}` },
      line.flaws && { key: `${line.lineId}:flaws`, text: `${line.title}: flaws noted — ${line.flaws}` },
      line.included && { key: `${line.lineId}:included`, text: `${line.title}: included — ${line.included}` },
    ].filter((entry): entry is { key: string; text: string } => Boolean(entry)),
  );

  function submitScan(event: React.FormEvent) {
    event.preventDefault();
    const match = lines.find((line) => !picked.has(line.lineId) && scanMatches(scan, { id: line.itemId, barcode: line.barcode, sku: line.sku }));
    if (match) {
      setPicked(new Set(picked).add(match.lineId));
      setScanError(null);
    } else {
      const other = lines.find((line) => scanMatches(scan, { id: line.itemId, barcode: line.barcode, sku: line.sku }));
      setScanError(other ? "Already picked." : "That tag is not on this order. Check the bin and scan again.");
    }
    setScan("");
  }

  const allPicked = picked.size === lines.length;
  const allChecked = checks.every((entry) => checked.has(entry.key));

  function markPacked() {
    startTransition(async () => {
      const result = await safely(() => advanceOrder(order.id, "markPacked"));
      setMessage({ ok: result.ok, text: result.message });
      if (result.ok) setStep(3);
    });
  }

  return (
    <div className="mx-auto max-w-5xl space-y-5">
      <div className="flex flex-wrap items-center gap-3">
        <Link href="/work?lane=pack" className="text-muted-foreground hover:text-foreground text-sm">
          ← Work
        </Link>
        <h1 className="text-xl font-semibold">Pack &amp; ship</h1>
        <span className="text-muted-foreground text-sm">
          Order {run.position} of {run.total} · {order.channel} {order.number}
        </span>
        {run.nextOrderId && step < 3 && (
          <Button asChild variant="ghost" size="sm" className="ml-auto">
            <Link href={`/work/pack?order=${run.nextOrderId}`}>Skip for now</Link>
          </Button>
        )}
      </div>

      <ol aria-label="Steps" className="grid grid-cols-4 gap-2">
        {STEPS.map((label, index) => (
          <li key={label}>
            <div className={`h-1 rounded-full ${index < step ? "bg-success" : index === step ? "bg-copper" : "bg-border"}`} />
            <span className={`mt-1.5 block text-xs ${index === step ? "font-semibold" : "text-muted-foreground"}`}>
              {index + 1}. {label}
            </span>
          </li>
        ))}
      </ol>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_300px]">
        <Card className="space-y-5 p-6">
          {step === 0 && (
            <>
              <h2 className="text-xl font-semibold">Pick {lines.length === 1 ? "the item" : `${lines.length} items`}</h2>
              <ul className="space-y-3">
                {lines.map((line) => {
                  const done = picked.has(line.lineId);
                  return (
                    <li key={line.lineId} className={`flex items-center gap-4 rounded-lg border p-3 ${done ? "border-success-border bg-success-surface" : ""}`}>
                      <span className="bg-muted flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-md border">
                        {line.photoUrl ? (
                          // eslint-disable-next-line @next/next/no-img-element
                          <img src={line.photoUrl} alt="" className="h-full w-full object-cover" />
                        ) : (
                          <ImageIcon className="text-muted-foreground h-5 w-5" aria-hidden />
                        )}
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="block font-medium">{line.title}</span>
                        <span className="text-muted-foreground block text-xs">{[line.sku, line.quantity > 1 ? `×${line.quantity}` : null].filter(Boolean).join(" · ")}</span>
                      </span>
                      <span className="shrink-0 text-right">
                        <span className="text-muted-foreground block text-[11px] uppercase tracking-wide">Bin</span>
                        {line.bin ? (
                          <span className="bg-primary text-primary-foreground rounded px-2 py-0.5 font-mono text-sm font-semibold">{line.bin}</span>
                        ) : (
                          <span className="text-muted-foreground text-sm">No bin set</span>
                        )}
                      </span>
                      <Button
                        type="button"
                        size="sm"
                        variant={done ? "secondary" : "default"}
                        aria-pressed={done}
                        onClick={() => {
                          const next = new Set(picked);
                          if (done) next.delete(line.lineId);
                          else next.add(line.lineId);
                          setPicked(next);
                        }}
                      >
                        {done ? (
                          <>
                            <Check className="mr-1.5 h-4 w-4" aria-hidden /> Picked
                          </>
                        ) : (
                          "Mark picked"
                        )}
                      </Button>
                    </li>
                  );
                })}
              </ul>
              {!allPicked && (
                <details className="rounded-md border px-3 py-2 text-sm">
                  <summary className="flex cursor-pointer items-center gap-2 font-medium">
                    <ScanLine className="h-4 w-4" aria-hidden /> Have a scanner? Scan tags instead
                  </summary>
                  <form onSubmit={submitScan} className="mt-3 space-y-2">
                    <Input aria-label="Scan a tag" value={scan} onChange={(event) => setScan(event.target.value)} placeholder="Barcode, SKU or tag code" className="h-11" />
                    <p className="text-muted-foreground text-xs">A scan picks the matching item and stops a wrong one. Optional — Mark picked works the same.</p>
                    {scanError && <p className="text-destructive text-sm">{scanError}</p>}
                  </form>
                </details>
              )}
            </>
          )}

          {step === 1 && (
            <>
              <h2 className="text-xl font-semibold">Check it matches the listing</h2>
              {checks.length === 0 ? (
                <p className="text-muted-foreground text-sm">The item records no condition notes, flaws or included pieces to check.</p>
              ) : (
                <ul className="space-y-2">
                  {checks.map((entry) => (
                    <li key={entry.key}>
                      <label className="flex min-h-11 cursor-pointer items-center gap-3 rounded-md border px-3 text-sm">
                        <input
                          type="checkbox"
                          checked={checked.has(entry.key)}
                          onChange={(event) => {
                            const next = new Set(checked);
                            if (event.target.checked) next.add(entry.key);
                            else next.delete(entry.key);
                            setChecked(next);
                          }}
                        />
                        {entry.text}
                      </label>
                    </li>
                  ))}
                </ul>
              )}
              <Button asChild variant="outline" size="sm">
                <Link href={`/orders/${order.id}`}>Found a problem — open the order</Link>
              </Button>
            </>
          )}

          {step === 2 && (
            <>
              <h2 className="text-xl font-semibold">{order.localPickup ? "Bag it for pickup" : "Pack it"}</h2>
              <p className="text-sm">
                {order.localPickup
                  ? "Label the bag with the order number and put it on the pickup shelf."
                  : order.channel === "Storefront"
                    ? "Pack and weigh it. Tracking is added on the next step, from the order."
                    : `${order.channel} supplies the label. Print it from ${order.channel}, then add the tracking number when it ships.`}
              </p>
              {message && !message.ok && <Notice tone="destructive">{message.text}</Notice>}
            </>
          )}

          {step === 3 && (
            <div className="space-y-3">
              <h2 className="text-xl font-semibold">Packed.</h2>
              <p className="text-sm">
                The order moves to <span className="font-semibold">{order.localPickup ? "Ready for pickup" : "Ship"}</span>. It closes itself when tracking shows it delivered.
              </p>
              <div className="flex flex-wrap gap-2">
                {run.nextOrderId ? (
                  <Button asChild>
                    <Link href={`/work/pack?order=${run.nextOrderId}`}>Next order →</Link>
                  </Button>
                ) : (
                  <Button asChild>
                    <Link href="/work?lane=ship">Go to Ship</Link>
                  </Button>
                )}
                <Button asChild variant="outline">
                  <Link href={`/orders/${order.id}`}>Open order</Link>
                </Button>
              </div>
            </div>
          )}

          {step < 3 && (
            <div className="flex gap-2 border-t pt-4">
              <Button type="button" variant="outline" disabled={step === 0 || pending} onClick={() => setStep(step - 1)}>
                Back
              </Button>
              {step === 0 && (
                <Button type="button" disabled={!allPicked} onClick={() => setStep(1)}>
                  {allPicked ? "All picked — next" : `${picked.size} of ${lines.length} picked`}
                </Button>
              )}
              {step === 1 && (
                <Button type="button" disabled={!allChecked} onClick={() => setStep(2)}>
                  All checked — next
                </Button>
              )}
              {step === 2 && (
                <Button type="button" disabled={pending} onClick={markPacked}>
                  {pending ? "Saving…" : "Mark packed"}
                </Button>
              )}
            </div>
          )}
        </Card>

        <aside className="space-y-3">
          <Card className="space-y-1.5 p-4 text-sm">
            <h3 className="mb-1 font-semibold">Order</h3>
            <Row label="Channel" value={order.channel} />
            <Row label="Number" value={order.number} />
            <Row label="Buyer" value={order.buyer ?? "—"} />
            <Row label={order.localPickup ? "Pickup" : "Ship to"} value={order.localPickup ? "Local pickup" : (order.shipTo ?? "—")} />
            <Row label="Total" value={formatCurrency(order.total)} />
            {order.note && <p className="bg-muted/50 mt-2 rounded p-2 text-xs">“{order.note}”</p>}
          </Card>
        </aside>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <span className="text-muted-foreground">{label}</span>
      <span className="truncate text-right">{value}</span>
    </div>
  );
}
