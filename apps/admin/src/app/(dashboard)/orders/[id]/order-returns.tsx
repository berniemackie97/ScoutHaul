"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Undo2 } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button } from "@overhaul/ui";
import {
  openReturn,
  issueReturnLabel,
  receiveReturn,
  resolveReturn,
  rejectReturn,
  cancelReturn,
  type OrderActionResult,
} from "./actions";

type ReturnDto = components["schemas"]["ReturnDto"];
type ReturnStatus = components["schemas"]["ReturnStatus"];

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";

const statusVariant = (status: ReturnStatus) => {
  if (status === "Refunded") return "success" as const;
  if (["Rejected", "Cancelled"].includes(status)) return "destructive" as const;
  if (status === "Received") return "info" as const;
  return "warning" as const; // Requested / LabelIssued
};

const OPEN: ReturnStatus[] = ["Requested", "LabelIssued", "Received"];

export function OrderReturns({
  saleId,
  orderId,
  soldPrice,
  returns,
}: {
  saleId: string;
  orderId: string;
  soldPrice: number;
  returns: ReturnDto[];
}) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [result, setResult] = useState<OrderActionResult | null>(null);

  const open = returns.find((r) => r.status && OPEN.includes(r.status));
  const history = returns.filter((r) => r !== open);

  function run(fn: () => Promise<OrderActionResult>) {
    startTransition(async () => {
      const next = await fn();
      setResult(next);
      if (next.ok) router.refresh();
    });
  }

  function num(label: string, fallback: number) {
    const raw = window.prompt(label, String(fallback));
    if (raw === null) return null;
    const parsed = Number(raw);
    return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
  }

  return (
    <div className="mt-2 space-y-1">
      {!open && (
        <button
          type="button"
          disabled={pending}
          onClick={() => {
            const reason = window.prompt("Reason for the return?");
            if (reason?.trim()) run(() => openReturn(saleId, orderId, reason.trim()));
          }}
          className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-xs"
        >
          <Undo2 className="h-3 w-3" /> Start return
        </button>
      )}

      {open && (
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <Badge variant={statusVariant(open.status!)}>{open.status}</Badge>
            {(open.returnShippingCost ?? 0) > 0 && (
              <span className="text-muted-foreground text-xs">label {money(open.returnShippingCost)}</span>
            )}
          </div>
          <div className="flex flex-wrap gap-1.5">
            {open.status === "Requested" && (
              <Action pending={pending} label="Issue label" onClick={() => {
                const cost = num("Return shipping cost you pay:", 0);
                if (cost !== null) run(() => issueReturnLabel(open.id!, orderId, cost));
              }} />
            )}
            {(open.status === "Requested" || open.status === "LabelIssued") && (
              <>
                <Action pending={pending} label="Mark received" onClick={() => run(() => receiveReturn(open.id!, orderId))} />
                <Action pending={pending} label="Cancel" onClick={() => run(() => cancelReturn(open.id!, orderId))} />
              </>
            )}
            {open.status === "Received" && (
              <>
                <Action pending={pending} label="Refund + restock" onClick={() => {
                  const amt = num("Item amount to refund:", soldPrice);
                  if (amt !== null) run(() => resolveReturn(open.id!, orderId, amt, true));
                }} />
                <Action pending={pending} label="Refund, keep item" onClick={() => {
                  const amt = num("Item amount to refund:", soldPrice);
                  if (amt !== null) run(() => resolveReturn(open.id!, orderId, amt, false));
                }} />
                <Action pending={pending} label="Reject" onClick={() => {
                  const reason = window.prompt("Reason for rejecting?");
                  if (reason?.trim()) run(() => rejectReturn(open.id!, orderId, reason.trim()));
                }} />
              </>
            )}
          </div>
        </div>
      )}

      {history.map((r) => (
        <div key={r.id} className="text-muted-foreground flex items-center gap-2 text-xs">
          <Badge variant={statusVariant(r.status!)}>{r.status}</Badge>
          {r.status === "Refunded" && r.restocked && <span>restocked</span>}
        </div>
      ))}

      {result && !result.ok && <p className="text-destructive text-xs">{result.message}</p>}
    </div>
  );
}

function Action({ label, onClick, pending }: { label: string; onClick: () => void; pending: boolean }) {
  return (
    <Button type="button" size="sm" variant="outline" disabled={pending} onClick={onClick} className="h-7 px-2 text-xs">
      {label}
    </Button>
  );
}
