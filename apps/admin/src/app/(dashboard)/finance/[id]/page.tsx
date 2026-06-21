import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, History } from "lucide-react";
import { Badge, Card, CardContent, CardHeader, CardTitle } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { SaleEditor } from "./sale-editor";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";

const dateTime = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value)) : "—";

function netFrom(json: string | null | undefined): number | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as { NetProfit?: number };
    return typeof parsed.NetProfit === "number" ? parsed.NetProfit : null;
  } catch {
    return null;
  }
}

export default async function SaleDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const api = await getApiClient();
  const [{ data: sale }, { data: corrections }] = await Promise.all([
    api.GET("/api/sales/{id}", { params: { path: { id } } }),
    api.GET("/api/sales/{id}/corrections", { params: { path: { id } } }),
  ]);
  if (!sale?.id) notFound();

  const lines = [
    { label: "Sold price", value: sale.soldPrice, strong: true },
    { label: "Cost of goods", value: -(sale.costOfGoods ?? 0) },
    { label: "Platform fees", value: -(sale.platformFees ?? 0) },
    { label: "Payment fees", value: -(sale.paymentFees ?? 0) },
    { label: "Shipping", value: -(sale.shippingCost ?? 0) },
    { label: "Supplies", value: -(sale.suppliesCost ?? 0) },
    { label: "Other costs", value: -(sale.otherCosts ?? 0) },
  ];

  return (
    <div className="space-y-6">
      <header className="space-y-3">
        <Link href="/finance" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Sales &amp; profit
        </Link>
        <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-end">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">{sale.inventoryItemTitle || "Sale"}</h1>
            <p className="mt-1 text-sm text-muted-foreground">{sale.platform} · sold {dateTime(sale.soldAt)}</p>
          </div>
          <Badge variant="secondary">{sale.status}</Badge>
        </div>
      </header>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_340px]">
        <SaleEditor sale={sale} />

        <aside className="space-y-6">
          <Card>
            <CardHeader><CardTitle className="text-lg">Profit breakdown</CardTitle></CardHeader>
            <CardContent className="space-y-2 text-sm">
              {lines.map((line) => (
                <div key={line.label} className="flex justify-between gap-4">
                  <span className="text-muted-foreground">{line.label}</span>
                  <span className={`tabular-nums ${line.strong ? "font-semibold" : ""}`}>{money(line.value)}</span>
                </div>
              ))}
              <div className="flex justify-between gap-4 border-t pt-2 text-base">
                <span className="font-medium">True net profit</span>
                <span className={`font-semibold tabular-nums ${(sale.netProfit ?? 0) < 0 ? "text-destructive" : ""}`}>{money(sale.netProfit)}</span>
              </div>
              <div className="mt-3 rounded-lg bg-muted/40 p-3">
                <div className="flex justify-between gap-4">
                  <span className="text-muted-foreground">Sales tax collected</span>
                  <span className="tabular-nums">{money(sale.salesTaxCollected)}</span>
                </div>
                <p className="mt-1 text-xs text-muted-foreground">
                  {sale.taxRemittedByPlatform
                    ? "Platform remits this tax — pass-through, not part of profit or liability."
                    : `You owe ${money(sale.salesTaxOwed)} of this to the state (tax liability).`}
                </p>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-lg"><History className="h-4 w-4" /> Correction history</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4 text-sm">
              {(corrections ?? []).length === 0 ? (
                <p className="text-muted-foreground">No corrections. This sale is unchanged since it was recorded.</p>
              ) : (
                (corrections ?? []).map((correction) => {
                  const before = netFrom(correction.beforeJson);
                  const after = netFrom(correction.afterJson);
                  return (
                    <div key={correction.id} className="border-l-2 border-border pl-3">
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-medium">#{correction.version}</span>
                        <span className="text-xs text-muted-foreground">{dateTime(correction.correctedAt)}</span>
                      </div>
                      <p className="mt-0.5 text-muted-foreground">{correction.reason}</p>
                      <p className="mt-1 text-xs text-muted-foreground">
                        by {correction.correctedBy}
                        {before !== null && after !== null && (
                          <> · net {money(before)} → <span className="font-medium text-foreground">{money(after)}</span></>
                        )}
                      </p>
                    </div>
                  );
                })
              )}
            </CardContent>
          </Card>
        </aside>
      </div>
    </div>
  );
}
