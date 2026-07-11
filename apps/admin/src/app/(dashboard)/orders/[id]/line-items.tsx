"use client";

import { Fragment, useState } from "react";
import Link from "next/link";
import { ChevronDown, ChevronRight } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button } from "@overhaul/ui";
import { RefundPanel } from "../sales/[id]/refund-panel";
import { OrderReturns } from "./order-returns";

type OrderLine = components["schemas"]["OrderLineDto"];
type Refund = components["schemas"]["RefundDto"];
type ReturnDto = components["schemas"]["ReturnDto"];

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";

const saleStatusVariant = (status: string | undefined) => {
  if (status === "Delivered") return "success" as const;
  if (["Returned", "Refunded", "Cancelled"].includes(status ?? "")) return "destructive" as const;
  if (["ReturnRequested", "PartiallyRefunded"].includes(status ?? "")) return "warning" as const;
  if (["Packed", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

export function LineItems({
  lines,
  orderId,
  refundsBySale,
  returnsBySale,
}: {
  lines: OrderLine[];
  orderId: string;
  refundsBySale: Record<string, Refund[]>;
  returnsBySale: Record<string, ReturnDto[]>;
}) {
  const [openId, setOpenId] = useState<string | null>(null);

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-muted/40 text-muted-foreground border-y text-left text-xs uppercase tracking-wide">
          <tr>
            <th className="px-4 py-2 font-medium">Item</th>
            <th className="px-4 py-2 font-medium">Sale</th>
            <th className="px-4 py-2 text-right font-medium">Item</th>
            <th className="px-4 py-2 text-right font-medium">Buyer ship</th>
            <th className="px-4 py-2 text-right font-medium">Fees</th>
            <th className="px-4 py-2 text-right font-medium">COGS</th>
            <th className="px-4 py-2 text-right font-medium">Refunded</th>
            <th className="px-4 py-2 text-right font-medium">Qty</th>
            <th className="px-4 py-2 text-right font-medium">True net</th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {lines.map((line) => {
            const sale = line.sale;
            const fees = (sale?.platformFees ?? 0) + (sale?.paymentFees ?? 0);
            const refunded = sale?.totalRefunded ?? 0;
            const open = sale?.id && openId === sale.id;
            return (
              <Fragment key={line.id}>
                <tr className={open ? "bg-muted/20" : undefined}>
                  <td className="px-4 py-3">
                    <Link
                      href={`/inventory/${line.inventoryItemId}`}
                      className="font-medium hover:underline"
                    >
                      {line.titleSnapshot}
                    </Link>
                    {line.skuSnapshot && (
                      <span className="text-muted-foreground block text-xs">SKU {line.skuSnapshot}</span>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    {sale?.id ? (
                      <div className="space-y-1.5">
                        <Badge variant={saleStatusVariant(sale.status)}>{sale.status}</Badge>
                        <Button
                          type="button"
                          size="sm"
                          variant="outline"
                          onClick={() => setOpenId(open ? null : sale.id!)}
                          className="h-7 gap-1 px-2 text-xs"
                        >
                          {open ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
                          Manage
                        </Button>
                      </div>
                    ) : (
                      <span className="text-muted-foreground text-xs">No sale line</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{money(line.unitPrice)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{money(sale?.shippingCharged)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {sale ? (fees > 0 ? `-${money(fees)}` : money(0)) : "—"}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {money(sale?.costOfGoods ?? line.unitCostSnapshot)}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {refunded > 0 ? (
                      <span className="text-amber-700">-{money(refunded)}</span>
                    ) : (
                      <span className="text-muted-foreground text-xs">—</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{line.quantity}</td>
                  <td
                    className={`px-4 py-3 text-right font-medium tabular-nums ${(sale?.netProfit ?? 0) < 0 ? "text-destructive" : ""}`}
                  >
                    {money(sale?.netProfit)}
                  </td>
                </tr>
                {open && sale?.id && (
                  <tr className="bg-muted/20">
                    <td colSpan={9} className="px-4 pb-6 pt-2">
                      <div className="space-y-4">
                        <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
                          <p className="text-muted-foreground text-xs uppercase tracking-wide">
                            Returns for this item
                          </p>
                          <OrderReturns
                            saleId={sale.id}
                            orderId={orderId}
                            soldPrice={sale.soldPrice ?? 0}
                            returns={returnsBySale[sale.id] ?? []}
                          />
                        </div>
                        <RefundPanel saleId={sale.id} refunds={refundsBySale[sale.id] ?? []} />
                        <Link
                          href={`/orders/sales/${sale.id}`}
                          className="text-brand-700 inline-block text-xs hover:underline"
                        >
                          Correct fees / price &amp; view full history →
                        </Link>
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
