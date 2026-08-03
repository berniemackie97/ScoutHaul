import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, Check } from "lucide-react";
import { Badge, Card, CardContent, CardHeader, CardTitle } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { CustomerEditor } from "./customer-editor";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";
const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";

const orderStatusVariant = (status: string | undefined) => {
  if (status === "Delivered") return "success" as const;
  if (["Cancelled", "Refunded"].includes(status ?? "")) return "destructive" as const;
  if (status === "Pending") return "warning" as const;
  if (["Paid", "Fulfilling", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

export default async function CustomerDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const api = await getApiClient();
  const { data: customer } = await api.GET("/api/customers/{id}", { params: { path: { id } } });
  if (!customer) notFound();

  const addresses = customer.addresses ?? [];
  const orders = customer.orders ?? [];

  const stats: Array<[string, string]> = [
    ["Lifetime spend", money(customer.lifetimeSpend)],
    ["Orders", String(customer.orderCount)],
    ["Store credit", money(customer.storeCreditBalance)],
    ["Last order", date(customer.lastOrderAt)],
  ];

  return (
    <div className="space-y-6">
      <div>
        <Link href="/customers" className="text-muted-foreground inline-flex items-center gap-1 text-sm hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Customers
        </Link>
        <div className="mt-2 flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold tracking-tight">{customer.displayName || customer.email}</h1>
          {customer.acceptsMarketing && (
            <Badge variant="secondary">
              <Check className="mr-1 h-3 w-3" /> Marketing opt-in
            </Badge>
          )}
        </div>
        <p className="text-muted-foreground mt-1 text-sm">
          {customer.email}
          {customer.phone ? ` · ${customer.phone}` : ""} · joined {date(customer.createdAt)}
        </p>
        <div className="mt-3">
          <CustomerEditor
            customer={{
              id,
              displayName: customer.displayName,
              email: customer.email,
              phone: customer.phone,
              acceptsMarketing: customer.acceptsMarketing,
            }}
          />
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-4">
        {stats.map(([label, value]) => (
          <Card key={label} className="p-5">
            <p className="text-muted-foreground text-sm">{label}</p>
            <p className="mt-2 text-2xl font-semibold tabular-nums">{value}</p>
          </Card>
        ))}
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Order history</CardTitle>
          </CardHeader>
          <CardContent className="overflow-x-auto p-0">
            {orders.length === 0 ? (
              <p className="text-muted-foreground p-6 text-sm">No orders yet.</p>
            ) : (
              <table className="w-full text-sm">
                <thead className="bg-muted/40 text-muted-foreground border-y text-left text-xs uppercase tracking-wide">
                  <tr>
                    <th className="px-4 py-2 font-medium">Order</th>
                    <th className="px-4 py-2 font-medium">Placed</th>
                    <th className="px-4 py-2 text-right font-medium">Total</th>
                    <th className="px-4 py-2 font-medium">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {orders.map((order) => (
                    <tr key={order.id} className="hover:bg-muted/30">
                      <td className="p-0">
                        <Link href={`/orders/${order.id}`} className="text-foreground block px-4 py-3 font-medium">
                          {order.orderNumber}
                        </Link>
                      </td>
                      <td className="text-muted-foreground px-4 py-3">{date(order.createdAt)}</td>
                      <td className="px-4 py-3 text-right tabular-nums">{money(order.grandTotal)}</td>
                      <td className="px-4 py-3">
                        <Badge variant={orderStatusVariant(order.status)}>{order.status}</Badge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </CardContent>
        </Card>

        <aside className="space-y-4">
          <Card className="p-5">
            <p className="text-muted-foreground text-xs uppercase tracking-wide">Addresses</p>
            {addresses.length === 0 ? (
              <p className="text-muted-foreground mt-2 text-sm">No saved addresses.</p>
            ) : (
              <div className="mt-3 space-y-3">
                {addresses.map((address) => (
                  <div key={address.id} className="text-sm">
                    <p className="flex items-center gap-2 font-medium">
                      {address.recipientName}
                      {address.isDefault && <Badge variant="secondary">Default</Badge>}
                    </p>
                    <p className="text-muted-foreground">
                      {[address.line1, address.line2, [address.city, address.region, address.postalCode].filter(Boolean).join(", "), address.country]
                        .filter(Boolean)
                        .join("\n")
                        .split("\n")
                        .map((part) => (
                          <span key={part} className="block">
                            {part}
                          </span>
                        ))}
                    </p>
                  </div>
                ))}
              </div>
            )}
          </Card>
        </aside>
      </div>
    </div>
  );
}
