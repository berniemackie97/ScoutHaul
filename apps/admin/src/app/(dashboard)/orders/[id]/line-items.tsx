"use client";

import { Fragment, useState } from "react";
import Link from "next/link";
import { ChevronDown, ChevronRight, History } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, formatCurrency, formatDateTime } from "@overhaul/ui";
import { SaleEditor } from "./sale-editor";
import { LineWorkspace } from "./line-workspace";

type OrderLine = components["schemas"]["OrderLineDto"];
type Refund = components["schemas"]["RefundDto"];
type ReturnDto = components["schemas"]["ReturnDto"];
type Channel = components["schemas"]["SaleChannelOptionDto"];
type Correction = components["schemas"]["SaleCorrectionDto"];

const dateTime = (value: string | null | undefined) =>
  formatDateTime(value);
function netFrom(json: string | null | undefined): number | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as { NetProfit?: number };
    return typeof parsed.NetProfit === "number" ? parsed.NetProfit : null;
  } catch {
    return null;
  }
}

// Fulfillment is shown once, on the order. A line only speaks up when its money did something unusual.
const EXCEPTION_LABELS: Record<string, { label: string; variant: "warning" | "destructive" | "secondary" }> = {
  ReturnRequested: { label: "Return requested", variant: "warning" },
  PartiallyRefunded: { label: "Partly refunded", variant: "warning" },
  Returned: { label: "Returned", variant: "destructive" },
  Refunded: { label: "Refunded", variant: "destructive" },
  Cancelled: { label: "Cancelled", variant: "secondary" },
};

const minus = (value: number) => (value > 0 ? `−${formatCurrency(value)}` : formatCurrency(0));

export function LineItems({
  lines,
  orderId,
  channels,
  refundsBySale,
  returnsBySale,
  correctionsBySale,
}: {
  lines: OrderLine[];
  orderId: string;
  channels: Channel[];
  refundsBySale: Record<string, Refund[]>;
  returnsBySale: Record<string, ReturnDto[]>;
  correctionsBySale: Record<string, Correction[]>;
}) {
  const [openId, setOpenId] = useState<string | null>(null);
  const anyRefund = lines.some((line) => (line.sale?.totalRefunded ?? 0) > 0);
  const columns = anyRefund ? 8 : 7;

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-muted/40 text-muted-foreground border-y text-left text-xs uppercase tracking-wide">
          <tr>
            <th className="min-w-[240px] px-4 py-2 font-medium">Item</th>
            <th className="whitespace-nowrap px-4 py-2 text-right font-medium">Price</th>
            <th className="whitespace-nowrap px-4 py-2 text-right font-medium">Buyer ship</th>
            <th className="whitespace-nowrap px-4 py-2 text-right font-medium">Fees</th>
            <th className="whitespace-nowrap px-4 py-2 text-right font-medium">COGS</th>
            {anyRefund && <th className="whitespace-nowrap px-4 py-2 text-right font-medium">Refunded</th>}
            <th className="whitespace-nowrap px-4 py-2 text-right font-medium">Net</th>
            <th className="w-px px-4 py-2" aria-label="Actions" />
          </tr>
        </thead>
        <tbody className="divide-y">
          {lines.map((line) => {
            const sale = line.sale;
            const fees = (sale?.platformFees ?? 0) + (sale?.paymentFees ?? 0);
            const refunded = sale?.totalRefunded ?? 0;
            const open = Boolean(sale?.id && openId === sale.id);
            const exception = sale?.status ? EXCEPTION_LABELS[sale.status] : undefined;
            return (
              <Fragment key={line.id}>
                <tr className={open ? "bg-muted/20" : undefined}>
                  <td className="px-4 py-3">
                    <Link href={`/inventory/${line.inventoryItemId}`} className="font-medium hover:underline">
                      {line.titleSnapshot}
                    </Link>
                    <span className="text-muted-foreground mt-0.5 flex flex-wrap items-center gap-2 text-xs">
                      {(line.quantity ?? 1) > 1 && <span>× {line.quantity}</span>}
                      {line.skuSnapshot && <span>SKU {line.skuSnapshot}</span>}
                      {exception && <Badge variant={exception.variant}>{exception.label}</Badge>}
                      {!sale && <span>No sale recorded for this line</span>}
                    </span>
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">{formatCurrency(line.unitPrice)}</td>
                  <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">{formatCurrency(sale?.shippingCharged)}</td>
                  <td className="text-muted-foreground whitespace-nowrap px-4 py-3 text-right tabular-nums">{sale ? minus(fees) : "—"}</td>
                  <td className="text-muted-foreground whitespace-nowrap px-4 py-3 text-right tabular-nums">
                    {minus(sale?.costOfGoods ?? line.unitCostSnapshot ?? 0)}
                  </td>
                  {anyRefund && (
                    <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">
                      {refunded > 0 ? <span className="text-warning">{minus(refunded)}</span> : <span className="text-muted-foreground">—</span>}
                    </td>
                  )}
                  <td
                    className={`whitespace-nowrap px-4 py-3 text-right font-semibold tabular-nums ${(sale?.netProfit ?? 0) < 0 ? "text-destructive" : ""}`}
                  >
                    {formatCurrency(sale?.netProfit)}
                  </td>
                  <td className="px-4 py-3 text-right">
                    {sale?.id && (
                      <Button
                        type="button"
                        size="sm"
                        variant={open ? "secondary" : "outline"}
                        onClick={() => setOpenId(open ? null : sale.id!)}
                        className="h-8 gap-1 whitespace-nowrap px-2.5 text-xs"
                        aria-expanded={open}
                      >
                        {open ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
                        Manage
                      </Button>
                    )}
                  </td>
                </tr>
                {open && sale?.id && (
                  <tr className="bg-muted/20">
                    <td colSpan={columns} className="px-4 pb-6 pt-2">
                      <div className="space-y-5">
                        <LineWorkspace
                          saleId={sale.id}
                          orderId={orderId}
                          soldPrice={sale.soldPrice ?? 0}
                          refunds={refundsBySale[sale.id] ?? []}
                          returns={returnsBySale[sale.id] ?? []}
                        />
                        <details className="group">
                          <summary className="text-muted-foreground hover:text-foreground cursor-pointer text-xs">
                            Correct fees or price, and see the history
                          </summary>
                          <div className="mt-3 space-y-4">
                            <SaleEditor sale={sale} channels={channels} />
                            <CorrectionHistory corrections={correctionsBySale[sale.id] ?? []} />
                          </div>
                        </details>
                      </div>
                    </td>
                  </tr>
                )}
              </Fragment>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function CorrectionHistory({ corrections }: { corrections: Correction[] }) {
  if (corrections.length === 0) {
    return <p className="text-muted-foreground text-xs">No corrections — unchanged since it was recorded.</p>;
  }
  return (
    <div className="space-y-3 rounded-xl border p-4">
      <p className="text-muted-foreground flex items-center gap-2 text-xs uppercase tracking-wide">
        <History className="h-3.5 w-3.5" /> Correction history
      </p>
      {corrections.map((correction) => {
        const before = netFrom(correction.beforeJson);
        const after = netFrom(correction.afterJson);
        return (
          <div key={correction.id} className="border-border border-l-2 pl-3 text-sm">
            <div className="flex items-center justify-between gap-2">
              <span className="font-medium">#{correction.version}</span>
              <span className="text-muted-foreground text-xs">{dateTime(correction.correctedAt)}</span>
            </div>
            <p className="text-muted-foreground mt-0.5">{correction.reason}</p>
            <p className="text-muted-foreground mt-1 text-xs">
              by {correction.correctedBy}
              {before !== null && after !== null && (
                <>
                  {" "}
                  · net {formatCurrency(before)} → <span className="text-foreground font-medium">{formatCurrency(after)}</span>
                </>
              )}
            </p>
          </div>
        );
      })}
    </div>
  );
}
