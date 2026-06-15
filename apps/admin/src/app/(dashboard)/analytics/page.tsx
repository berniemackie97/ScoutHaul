"use client";

import {
  DollarSign, TrendingUp, Package, BarChart3, PieChart,
} from "lucide-react";
import {
  Card, CardContent, CardHeader, CardTitle, CardDescription,
  Tabs, TabsList, TabsTrigger, TabsContent,
  formatCurrency,
} from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

export default function AnalyticsPage() {
  const { data: margins } = trpc.analytics.margins.useQuery();
  const { data: inventoryValue } = trpc.analytics.inventoryValue.useQuery();

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-2xl font-bold">Analytics</h1>
        <p className="text-sm text-muted-foreground">Financial performance and inventory insights</p>
      </div>

      <Tabs defaultValue="financial">
        <TabsList className="mb-6">
          <TabsTrigger value="financial">Financial</TabsTrigger>
          <TabsTrigger value="inventory">Inventory</TabsTrigger>
          <TabsTrigger value="margins">Margins</TabsTrigger>
        </TabsList>

        <TabsContent value="financial">
          <div className="grid sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
            <Card>
              <CardContent className="p-6">
                <p className="text-sm text-muted-foreground">Total Revenue (Sold)</p>
                <p className="text-2xl font-bold mt-1">{formatCurrency(margins?.totalRevenue ?? 0)}</p>
              </CardContent>
            </Card>
            <Card>
              <CardContent className="p-6">
                <p className="text-sm text-muted-foreground">Total COGS</p>
                <p className="text-2xl font-bold mt-1">{formatCurrency(margins?.totalCost ?? 0)}</p>
              </CardContent>
            </Card>
            <Card>
              <CardContent className="p-6">
                <p className="text-sm text-muted-foreground">Gross Profit</p>
                <p className="text-2xl font-bold mt-1 text-green-600">{formatCurrency(margins?.grossProfit ?? 0)}</p>
              </CardContent>
            </Card>
            <Card>
              <CardContent className="p-6">
                <p className="text-sm text-muted-foreground">Overall Margin</p>
                <p className="text-2xl font-bold mt-1">{(margins?.overallMargin ?? 0).toFixed(1)}%</p>
              </CardContent>
            </Card>
          </div>

          <Card>
            <CardHeader>
              <CardTitle>Revenue by Source Type</CardTitle>
              <CardDescription>Profitability breakdown by inventory source</CardDescription>
            </CardHeader>
            <CardContent>
              {margins?.bySource && Object.keys(margins.bySource).length > 0 ? (
                <div className="space-y-4">
                  {Object.entries(margins.bySource).map(([source, data]) => {
                    const margin = data.revenue > 0 ? ((data.revenue - data.cost) / data.revenue * 100) : 0;
                    return (
                      <div key={source} className="flex items-center justify-between py-2 border-b last:border-0">
                        <div>
                          <p className="text-sm font-medium">{source.replace(/_/g, " ")}</p>
                          <p className="text-xs text-muted-foreground">{data.count} items sold</p>
                        </div>
                        <div className="text-right">
                          <p className="text-sm font-medium">{formatCurrency(data.revenue)}</p>
                          <p className={`text-xs ${margin >= 50 ? "text-green-600" : "text-yellow-600"}`}>
                            {margin.toFixed(0)}% margin
                          </p>
                        </div>
                      </div>
                    );
                  })}
                </div>
              ) : (
                <p className="text-sm text-muted-foreground text-center py-8">
                  No sales data yet. Margins will appear once items are sold.
                </p>
              )}
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="inventory">
          <div className="grid sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
            <Card>
              <CardContent className="p-6">
                <p className="text-sm text-muted-foreground">Total Items</p>
                <p className="text-2xl font-bold mt-1">{inventoryValue?.totalItems ?? 0}</p>
              </CardContent>
            </Card>
            <Card>
              <CardContent className="p-6">
                <p className="text-sm text-muted-foreground">Inventory at Cost</p>
                <p className="text-2xl font-bold mt-1">{formatCurrency(Number(inventoryValue?.totalCost ?? 0))}</p>
              </CardContent>
            </Card>
            <Card>
              <CardContent className="p-6">
                <p className="text-sm text-muted-foreground">Retail Value</p>
                <p className="text-2xl font-bold mt-1">{formatCurrency(Number(inventoryValue?.totalRetail ?? 0))}</p>
              </CardContent>
            </Card>
            <Card>
              <CardContent className="p-6">
                <p className="text-sm text-muted-foreground">Potential Profit</p>
                <p className="text-2xl font-bold mt-1 text-green-600">
                  {formatCurrency(inventoryValue?.potentialProfit ?? 0)}
                </p>
              </CardContent>
            </Card>
          </div>

          <Card>
            <CardHeader>
              <CardTitle>Inventory by Category</CardTitle>
            </CardHeader>
            <CardContent>
              {inventoryValue?.byCategory.length === 0 ? (
                <p className="text-sm text-muted-foreground text-center py-8">No inventory data</p>
              ) : (
                <div className="space-y-3">
                  {inventoryValue?.byCategory.map((cat) => (
                    <div key={cat.categoryId} className="flex items-center justify-between py-2 border-b last:border-0">
                      <div>
                        <p className="text-sm font-medium">{cat.category?.name ?? "Unknown"}</p>
                        <p className="text-xs text-muted-foreground">{cat._count} items</p>
                      </div>
                      <div className="text-right">
                        <p className="text-sm">{formatCurrency(Number(cat._sum.listPrice ?? 0))} retail</p>
                        <p className="text-xs text-muted-foreground">{formatCurrency(Number(cat._sum.costPrice ?? 0))} cost</p>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="margins">
          <Card>
            <CardHeader>
              <CardTitle>Margin Analysis</CardTitle>
              <CardDescription>Target: 50-60% gross margins</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="space-y-6">
                <div>
                  <div className="flex justify-between text-sm mb-1">
                    <span>Overall Margin</span>
                    <span className="font-bold">{(margins?.overallMargin ?? 0).toFixed(1)}%</span>
                  </div>
                  <div className="h-4 bg-muted rounded-full overflow-hidden">
                    <div
                      className="h-full bg-gradient-to-r from-red-500 via-yellow-500 to-green-500 rounded-full"
                      style={{ width: `${Math.min(margins?.overallMargin ?? 0, 100)}%` }}
                    />
                  </div>
                  <div className="flex justify-between text-xs text-muted-foreground mt-1">
                    <span>0%</span>
                    <span>Target: 50-60%</span>
                    <span>100%</span>
                  </div>
                </div>

                <div className="text-sm text-muted-foreground">
                  <p>{margins?.itemsSold ?? 0} items sold total</p>
                  <p>Revenue: {formatCurrency(margins?.totalRevenue ?? 0)}</p>
                  <p>COGS: {formatCurrency(margins?.totalCost ?? 0)}</p>
                  <p>Gross Profit: {formatCurrency(margins?.grossProfit ?? 0)}</p>
                </div>
              </div>
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </div>
  );
}
