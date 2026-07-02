import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { Badge, Card, CardContent, CardHeader, CardTitle } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { OrderStatusControl } from "./order-status-control";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";
const dateTime = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value)) : "—";

const orderStatusVariant = (status: string | undefined) => {
  if (status === "Delivered") return "success" as const;
  if (["Cancelled", "Refunded"].includes(status ?? "")) return "destructive" as const;
  if (status === "Pending") return "warning" as const;
  if (["Paid", "Fulfilling", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

type OrderStatus = "Pending" | "Paid" | "Fulfilling" | "Shipped" | "Delivered" | "Cancelled" | "Refunded";

export default async function OrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const api = await getApiClient();
  const { data: order } = await api.GET("/api/orders/{id}", { params: { path: { id } } });
  if (!order) notFound();

  const lines = order.lines ?? [];
  const shipTo = [order.shipToLine1, order.shipToLine2, [order.shipToCity, order.shipToRegion, order.shipToPostalCode].filter(Boolean).join(", "), order.shipToCountry]
    .filter(Boolean)
    .join("\n");
  const totals: Array<[string, number | null | undefined, boolean?]> = [
    ["Subtotal", order.subtotal],
    ["Discounts", order.discountTotal != null ? -order.discountTotal : null],
    ["Tax", order.taxTotal],
    ["Shipping", order.shippingTotal],
    ["Total", order.grandTotal, true],
  ];

  return (
    <div className="space-y-6">
      <div>
        <Link href="/orders" className="text-muted-foreground inline-flex items-center gap-1 text-sm hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Orders
        </Link>
        <div className="mt-2 flex flex-col justify-between gap-3 sm:flex-row sm:items-start">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">{order.orderNumber}</h1>
            <p className="text-muted-foreground mt-1 text-sm">Placed {dateTime(order.createdAt)}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Badge variant={orderStatusVariant(order.status)}>{order.status}</Badge>
            <Badge variant={order.paymentStatus === "Paid" ? "success" : order.paymentStatus === "Refunded" ? "destructive" : "secondary"}>
              {order.paymentStatus}
            </Badge>
            <Badge variant={order.fulfillmentStatus === "Fulfilled" ? "success" : "secondary"}>{order.fulfillmentStatus}</Badge>
          </div>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Line items</CardTitle>
            </CardHeader>
            <CardContent className="p-0">
              <table className="w-full text-sm">
                <thead className="bg-muted/40 text-muted-foreground border-y text-left text-xs uppercase tracking-wide">
                  <tr>
                    <th className="px-4 py-2 font-medium">Item</th>
                    <th className="px-4 py-2 text-right font-medium">Unit</th>
                    <th className="px-4 py-2 text-right font-medium">Qty</th>
                    <th className="px-4 py-2 text-right font-medium">Line</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {lines.map((line) => (
                    <tr key={line.id}>
                      <td className="px-4 py-3">
                        <Link href={`/inventory/${line.inventoryItemId}`} className="font-medium hover:underline">
                          {line.titleSnapshot}
                        </Link>
                        {line.skuSnapshot && <span className="text-muted-foreground block text-xs">SKU {line.skuSnapshot}</span>}
                      </td>
                      <td className="px-4 py-3 text-right tabular-nums">{money(line.unitPrice)}</td>
                      <td className="px-4 py-3 text-right tabular-nums">{line.quantity}</td>
                      <td className="px-4 py-3 text-right font-medium tabular-nums">{money(line.lineTotal)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Advance order</CardTitle>
            </CardHeader>
            <CardContent>
              <OrderStatusControl id={order.id!} status={(order.status ?? "Pending") as OrderStatus} />
            </CardContent>
          </Card>
        </div>

        <aside className="space-y-4">
          <Card className="p-5">
            <p className="text-muted-foreground text-sm">Order total</p>
            <p className="mt-1 text-3xl font-semibold tabular-nums">{money(order.grandTotal)}</p>
            <div className="mt-4 space-y-1 border-t pt-3 text-sm">
              {totals.map(([label, value, strong]) => (
                <div key={label} className={`flex justify-between ${strong ? "border-t pt-2 font-semibold" : ""}`}>
                  <span className="text-muted-foreground">{label}</span>
                  <span className="tabular-nums">{money(value)}</span>
                </div>
              ))}
              <div className="text-muted-foreground flex justify-between pt-2 text-xs">
                <span>Merchandise margin (ex tax/ship)</span>
                <span className="tabular-nums">{money(order.grossMargin)}</span>
              </div>
            </div>
          </Card>

          <Card className="p-5 text-sm">
            <p className="text-muted-foreground text-xs uppercase tracking-wide">Customer</p>
            <p className="mt-1 font-medium">{order.customerName || order.email}</p>
            <p className="text-muted-foreground">{order.email}</p>
            {order.customerId && (
              <Link href={`/customers/${order.customerId}`} className="text-brand-700 mt-2 inline-block text-sm hover:underline">
                View customer
              </Link>
            )}
          </Card>

          <Card className="p-5 text-sm">
            <p className="text-muted-foreground text-xs uppercase tracking-wide">{order.isLocalPickup ? "Local pickup" : "Ship to"}</p>
            {order.isLocalPickup ? (
              <p className="mt-1">Customer is collecting in person.</p>
            ) : shipTo ? (
              <>
                {order.shipToName && <p className="mt-1 font-medium">{order.shipToName}</p>}
                <p className="text-muted-foreground whitespace-pre-line">{shipTo}</p>
              </>
            ) : (
              <p className="text-muted-foreground mt-1">No shipping address on file.</p>
            )}
            {order.customerNote && <p className="text-muted-foreground mt-3 border-t pt-3 italic">“{order.customerNote}”</p>}
          </Card>
        </aside>
      </div>
    </div>
  );
}
