"use client";

import { useEffect, useMemo, useState, useTransition } from "react";
import { useActionState } from "react";
import { useRouter } from "next/navigation";
import { useFormStatus } from "react-dom";
import { CornerUpLeft, RotateCcw, Check } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Input, Label, Textarea, formatCurrency, formatDate } from "@overhaul/ui";
import { recordRefund, voidRefund, type SaleActionResult } from "./sale-actions";
import {
  openReturn,
  issueReturnLabel,
  receiveReturn,
  resolveReturn,
  rejectReturn,
  cancelReturn,
  type OrderActionResult,
} from "./actions";

type Refund = components["schemas"]["RefundDto"];
type ReturnDto = components["schemas"]["ReturnDto"];
type ReturnStatus = components["schemas"]["ReturnStatus"];

const money = (value: number | null | undefined) =>
  formatCurrency(value ?? 0);
const day = (value: string | null | undefined) =>
  formatDate(value);
const today = new Date().toISOString().slice(0, 10);
const OPEN_STATUSES: ReturnStatus[] = ["Requested", "LabelIssued", "Received"];
const STAGES: { key: ReturnStatus; label: string }[] = [
  { key: "Requested", label: "Requested" },
  { key: "LabelIssued", label: "Label sent" },
  { key: "Received", label: "Received" },
  { key: "Refunded", label: "Refunded" },
];

export function LineWorkspace({
  saleId,
  orderId,
  soldPrice,
  refunds,
  returns,
}: {
  saleId: string;
  orderId: string;
  soldPrice: number;
  refunds: Refund[];
  returns: ReturnDto[];
}) {
  const refundedItem = refunds.reduce((sum, r) => sum + (r.itemAmount ?? 0), 0);
  const remaining = Math.max(0, Math.round((soldPrice - refundedItem) * 100) / 100);
  const openReturn_ = returns.find((r) => r.status && OPEN_STATUSES.includes(r.status));
  const history = returns.filter((r) => r !== openReturn_);
  const [mode, setMode] = useState<"idle" | "refund" | "return">("idle");

  return (
    <div className="space-y-4">
      <RefundHistory refunds={refunds} saleId={saleId} orderId={orderId} />

      {openReturn_ ? (
        <ReturnFlow rma={openReturn_} orderId={orderId} saleId={saleId} remaining={remaining} />
      ) : mode === "refund" ? (
        <RefundForm
          saleId={saleId}
          orderId={orderId}
          remaining={remaining}
          onClose={() => setMode("idle")}
        />
      ) : mode === "return" ? (
        <ReturnRequestForm saleId={saleId} orderId={orderId} onClose={() => setMode("idle")} />
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          <Button type="button" size="sm" disabled={remaining <= 0} onClick={() => setMode("refund")}>
            Issue refund
          </Button>
          <Button type="button" size="sm" variant="outline" onClick={() => setMode("return")}>
            <CornerUpLeft className="mr-1.5 h-3.5 w-3.5" /> Start return
          </Button>
          {remaining <= 0 && (
            <span className="text-muted-foreground text-xs">Fully refunded.</span>
          )}
        </div>
      )}

      {history.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {history.map((r) => (
            <Badge key={r.id} variant={r.status === "Refunded" ? "success" : "secondary"}>
              Return {r.status}
              {r.status === "Refunded" && r.restocked ? " · restocked" : ""}
            </Badge>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Refund (no return): one form, partial vs full derived from the amount ───────────────────────
function RefundForm({
  saleId,
  remaining,
  restockContext,
  onClose,
}: {
  saleId: string;
  orderId: string;
  remaining: number;
  restockContext?: boolean;
  onClose: () => void;
}) {
  const router = useRouter();
  const [state, action] = useActionState<SaleActionResult, FormData>(
    recordRefund.bind(null, saleId),
    { ok: false, message: "" },
  );
  const [amount, setAmount] = useState(remaining.toFixed(2));
  const [advanced, setAdvanced] = useState(false);
  const fieldErrors = state.fieldErrors ?? {};
  const isFull = Number(amount) >= remaining - 0.001 && Number(amount) > 0;

  useEffect(() => {
    if (state.ok) {
      router.refresh();
      onClose();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state]);

  return (
    <form action={action} className="bg-card space-y-3 rounded-xl border p-4">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold">Issue refund</p>
        <span className="text-muted-foreground text-xs">Refundable {money(remaining)}</span>
      </div>
      {/* item amount drives partial vs full — no separate mode to pick */}
      <input type="hidden" name="full" value={isFull ? "on" : "off"} />
      <div className="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
        <div className="grid gap-1.5">
          <Label htmlFor="itemAmount">Item amount to refund</Label>
          <Input
            id="itemAmount"
            name="itemAmount"
            type="number"
            step="0.01"
            min="0"
            max={remaining}
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
          />
          {fieldErrors.itemAmount && <p className="text-destructive text-xs">{fieldErrors.itemAmount}</p>}
        </div>
        <Badge variant={isFull ? "destructive" : "warning"} className="mb-2 justify-self-start">
          {isFull ? "Full refund" : "Partial refund"}
        </Badge>
      </div>
      <div className="grid gap-1.5">
        <Label htmlFor="reason">Reason</Label>
        <Input id="reason" name="reason" placeholder="e.g. Item not as described — buyer kept it" />
        {fieldErrors.reason && <p className="text-destructive text-xs">{fieldErrors.reason}</p>}
      </div>

      <button
        type="button"
        onClick={() => setAdvanced((v) => !v)}
        className="text-muted-foreground hover:text-foreground text-xs underline"
      >
        {advanced ? "Hide" : "Adjust"} shipping, fee credit &amp; tax
      </button>
      {advanced && (
        <div className="grid gap-3 sm:grid-cols-3">
          <Field name="shippingRefunded" label="Shipping refunded" />
          <Field name="feeCredit" label="Platform fee credited" />
          <Field name="taxRefunded" label="Tax refunded" />
        </div>
      )}
      <input type="hidden" name="refundedAt" value={today} />
      {restockContext && (
        <p className="text-muted-foreground text-xs">
          To restock the item, use the return flow instead — a no-return refund leaves it with the buyer.
        </p>
      )}

      <div className="flex items-center justify-between gap-3 pt-1">
        {state.message && !state.ok && <p className="text-destructive text-sm">{state.message}</p>}
        <div className="ml-auto flex gap-2">
          <Button type="button" variant="ghost" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <SubmitButton label={`Refund ${money(Number(amount) || 0)}`} pendingLabel="Refunding…" />
        </div>
      </div>
    </form>
  );
}

function Field({ name, label }: { name: string; label: string }) {
  return (
    <div className="grid gap-1.5">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type="number" step="0.01" min="0" placeholder="0.00" />
    </div>
  );
}

// ── Return request ──────────────────────────────────────────────────────────────────────────────
function ReturnRequestForm({
  saleId,
  orderId,
  onClose,
}: {
  saleId: string;
  orderId: string;
  onClose: () => void;
}) {
  const router = useRouter();
  const [pending, start] = useTransition();
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  function submit() {
    if (!reason.trim()) {
      setError("Give a reason for the return.");
      return;
    }
    start(async () => {
      const r = await openReturn(saleId, orderId, reason.trim());
      if (r.ok) router.refresh();
      else setError(r.message);
    });
  }

  return (
    <div className="bg-card space-y-3 rounded-xl border p-4">
      <p className="text-sm font-semibold">Start a return</p>
      <div className="grid gap-1.5">
        <Label htmlFor="returnReason">Why is the buyer returning it?</Label>
        <Textarea
          id="returnReason"
          rows={2}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="e.g. Doesn't fit — buyer requested a return"
        />
        {error && <p className="text-destructive text-xs">{error}</p>}
      </div>
      <p className="text-muted-foreground text-xs">
        Opens the return, then you send the label, receive the item, and refund on arrival.
      </p>
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" size="sm" onClick={onClose}>
          Cancel
        </Button>
        <Button type="button" size="sm" disabled={pending} onClick={submit}>
          {pending ? "Opening…" : "Open return"}
        </Button>
      </div>
    </div>
  );
}

// ── Open return lifecycle ───────────────────────────────────────────────────────────────────────
function ReturnFlow({
  rma,
  orderId,
  remaining,
}: {
  rma: ReturnDto;
  orderId: string;
  saleId: string;
  remaining: number;
}) {
  const router = useRouter();
  const [pending, start] = useTransition();
  const [error, setError] = useState<string | null>(null);
  const [labelCost, setLabelCost] = useState("");
  const [refundOpen, setRefundOpen] = useState(false);
  const currentIndex = useMemo(
    () => STAGES.findIndex((s) => s.key === rma.status),
    [rma.status],
  );

  function run(fn: () => Promise<OrderActionResult>) {
    setError(null);
    start(async () => {
      const r = await fn();
      if (r.ok) router.refresh();
      else setError(r.message);
    });
  }

  return (
    <div className="bg-card space-y-4 rounded-xl border p-4">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm font-semibold">Return in progress</p>
        <span className="text-muted-foreground text-xs">Opened {day(rma.requestedAt)}</span>
      </div>

      {/* stage tracker */}
      <ol className="flex items-center gap-1 text-xs">
        {STAGES.map((stage, i) => {
          const done = i < currentIndex || rma.status === "Refunded";
          const active = i === currentIndex;
          return (
            <li key={stage.key} className="flex flex-1 items-center gap-1">
              <span
                className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[10px] font-semibold ${
                  done
                    ? "bg-success text-white"
                    : active
                      ? "bg-brand-600 text-white"
                      : "bg-muted text-muted-foreground"
                }`}
              >
                {done ? <Check className="h-3 w-3" /> : i + 1}
              </span>
              <span className={active ? "font-medium" : "text-muted-foreground"}>{stage.label}</span>
              {i < STAGES.length - 1 && <span className="bg-border mx-1 h-px flex-1" />}
            </li>
          );
        })}
      </ol>

      <p className="text-muted-foreground text-xs">Reason: {rma.reason}</p>
      {(rma.returnShippingCost ?? 0) > 0 && (
        <p className="text-muted-foreground text-xs">Return label cost {money(rma.returnShippingCost)}</p>
      )}

      {/* stage-specific next action */}
      {rma.status === "Requested" && (
        <div className="flex flex-wrap items-end gap-2">
          <div className="grid gap-1">
            <Label htmlFor="labelCost" className="text-xs">Return label cost you pay</Label>
            <Input
              id="labelCost"
              type="number"
              step="0.01"
              min="0"
              value={labelCost}
              onChange={(e) => setLabelCost(e.target.value)}
              placeholder="0.00"
              className="h-8 w-32"
            />
          </div>
          <Button
            type="button"
            size="sm"
            disabled={pending}
            onClick={() => run(() => issueReturnLabel(rma.id!, orderId, Number(labelCost) || 0))}
          >
            Send label
          </Button>
          <Button type="button" size="sm" variant="outline" disabled={pending} onClick={() => run(() => receiveReturn(rma.id!, orderId))}>
            Mark received
          </Button>
        </div>
      )}
      {rma.status === "LabelIssued" && (
        <Button type="button" size="sm" disabled={pending} onClick={() => run(() => receiveReturn(rma.id!, orderId))}>
          Mark received back
        </Button>
      )}
      {rma.status === "Received" && !refundOpen && (
        <div className="flex flex-wrap gap-2">
          <Button type="button" size="sm" onClick={() => setRefundOpen(true)}>
            <RotateCcw className="mr-1.5 h-3.5 w-3.5" /> Refund &amp; restock
          </Button>
        </div>
      )}
      {rma.status === "Received" && refundOpen && (
        <ReturnRefund rma={rma} orderId={orderId} remaining={remaining} onClose={() => setRefundOpen(false)} />
      )}

      <div className="flex flex-wrap items-center gap-3 border-t pt-3">
        <span className="text-muted-foreground text-xs">Exceptions:</span>
        <button
          type="button"
          disabled={pending}
          onClick={() => {
            const reason = window.prompt("Reason for rejecting the return?");
            if (reason?.trim()) run(() => rejectReturn(rma.id!, orderId, reason.trim()));
          }}
          className="text-muted-foreground hover:text-destructive text-xs underline"
        >
          Reject
        </button>
        <button
          type="button"
          disabled={pending}
          onClick={() => run(() => cancelReturn(rma.id!, orderId))}
          className="text-muted-foreground hover:text-foreground text-xs underline"
        >
          Cancel return
        </button>
      </div>
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

// Refund issued once the item is back — amount defaults to full, restock is implied by the return.
function ReturnRefund({
  rma,
  orderId,
  remaining,
  onClose,
}: {
  rma: ReturnDto;
  orderId: string;
  remaining: number;
  onClose: () => void;
}) {
  const router = useRouter();
  const [pending, start] = useTransition();
  const [amount, setAmount] = useState(remaining.toFixed(2));
  const [error, setError] = useState<string | null>(null);
  const isFull = Number(amount) >= remaining - 0.001 && Number(amount) > 0;

  return (
    <div className="bg-muted/30 space-y-3 rounded-lg p-3">
      <div className="flex items-end gap-2">
        <div className="grid gap-1">
          <Label htmlFor="returnRefundAmt" className="text-xs">Item amount to refund</Label>
          <Input
            id="returnRefundAmt"
            type="number"
            step="0.01"
            min="0"
            max={remaining}
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className="h-8 w-32"
          />
        </div>
        <Badge variant={isFull ? "destructive" : "warning"} className="mb-1.5">
          {isFull ? "Full" : "Partial"}
        </Badge>
      </div>
      <p className="text-muted-foreground text-xs">The item returns to sellable inventory.</p>
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" size="sm" onClick={onClose}>
          Cancel
        </Button>
        <Button
          type="button"
          size="sm"
          disabled={pending}
          onClick={() => {
            setError(null);
            start(async () => {
              const r = await resolveReturn(rma.id!, orderId, Number(amount) || 0, true, isFull ? "Full" : "Partial");
              if (r.ok) router.refresh();
              else setError(r.message);
            });
          }}
        >
          {pending ? "Refunding…" : `Refund ${money(Number(amount) || 0)} + restock`}
        </Button>
      </div>
      {error && <p className="text-destructive text-xs">{error}</p>}
    </div>
  );
}

// ── Refund history ──────────────────────────────────────────────────────────────────────────────
function RefundHistory({ refunds, saleId }: { refunds: Refund[]; saleId: string; orderId: string }) {
  const router = useRouter();
  const [pending, start] = useTransition();
  if (refunds.length === 0) return null;
  return (
    <div className="space-y-1.5">
      {refunds.map((refund) => (
        <div key={refund.id} className="bg-muted/30 flex items-center justify-between gap-3 rounded-lg px-3 py-2 text-sm">
          <div className="flex items-center gap-2">
            <Badge variant={refund.type === "Full" ? "destructive" : "warning"}>{refund.type}</Badge>
            <span className="font-medium tabular-nums">
              {money((refund.itemAmount ?? 0) + (refund.shippingRefunded ?? 0))}
            </span>
            {refund.itemReturned && <span className="text-muted-foreground text-xs">· restocked</span>}
            <span className="text-muted-foreground text-xs">{day(refund.refundedAt)} · {refund.reason}</span>
          </div>
          <button
            type="button"
            disabled={pending}
            onClick={() => start(async () => { await voidRefund(saleId, refund.id ?? ""); router.refresh(); })}
            className="text-muted-foreground hover:text-destructive text-xs underline"
          >
            Void
          </button>
        </div>
      ))}
    </div>
  );
}

function SubmitButton({ label, pendingLabel }: { label: string; pendingLabel: string }) {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" size="sm" disabled={pending}>
      {pending ? pendingLabel : label}
    </Button>
  );
}
