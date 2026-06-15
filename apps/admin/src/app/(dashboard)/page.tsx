"use client";

import {
  DollarSign, ShoppingCart, Package, Users,
  Wrench, TrendingUp, ArrowUpRight, ArrowDownRight,
} from "lucide-react";
import {
  Card, CardContent, CardHeader, CardTitle, CardDescription,
  Badge, Button, formatCurrency, formatDate,
} from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

function KPICard({
  title, value, change, icon: Icon, trend,
}: {
  title: string;
  value: string;
  change?: string;
  icon: React.ElementType;
  trend?: "up" | "down";
}) {
  return (
    <Card>
      <CardContent className="p-6">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm text-muted-foreground">{title}</p>
            <p className="text-2xl font-bold mt-1">{value}</p>
            {change && (
              <div className="flex items-center gap-1 mt-1">
                {trend === "up" ? (
                  <ArrowUpRight className="h-3 w-3 text-green-500" />
                ) : (
                  <ArrowDownRight className="h-3 w-3 text-red-500" />
                )}
                <span className={`text-xs ${trend === "up" ? "text-green-500" : "text-red-500"}`}>
                  {change}
                </span>
              </div>
            )}
          </div>
          <div className="h-12 w-12 rounded-xl bg-brand-100 dark:bg-brand-900 flex items-center justify-center">
            <Icon className="h-6 w-6 text-brand-600" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

const statusColors: Record<string, string> = {
  PENDING: "warning",
  CONFIRMED: "info",
  PROCESSING: "info",
  SHIPPED: "default",
  DELIVERED: "success",
  CANCELLED: "destructive",
};

export default function DashboardPage() {
  const { data: dashboard, isLoading } = trpc.analytics.dashboard.useQuery();

  if (isLoading) {
    return (
      <div>
        <h1 className="text-2xl font-bold mb-6">Dashboard</h1>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
          {[1, 2, 3, 4].map((i) => (
            <Card key={i}>
              <CardContent className="p-6 animate-pulse">
                <div className="h-4 bg-muted rounded w-1/2 mb-2" />
                <div className="h-8 bg-muted rounded w-1/3" />
              </CardContent>
            </Card>
          ))}
        </div>
      </div>
    );
  }

  const kpis = dashboard?.kpis;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">Dashboard</h1>
          <p className="text-sm text-muted-foreground">
            Overhaul Trading Company overview — last 30 days
          </p>
        </div>
        <Badge variant="outline">Live Data</Badge>
      </div>

      {/* KPIs */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        <KPICard
          title="Revenue (30d)"
          value={formatCurrency(Number(kpis?.totalRevenue30d ?? 0))}
          change="+12% vs last month"
          icon={DollarSign}
          trend="up"
        />
        <KPICard
          title="Orders (30d)"
          value={String(kpis?.orders30d ?? 0)}
          change="+8 from last week"
          icon={ShoppingCart}
          trend="up"
        />
        <KPICard
          title="Active Products"
          value={String(kpis?.activeProducts ?? 0)}
          icon={Package}
        />
        <KPICard
          title="Customers"
          value={String(kpis?.totalCustomers ?? 0)}
          change="+5 this month"
          icon={Users}
          trend="up"
        />
      </div>

      {/* Secondary KPIs */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-8">
        <KPICard
          title="Avg Order Value"
          value={formatCurrency(kpis?.avgOrderValue ?? 0)}
          icon={TrendingUp}
        />
        <KPICard
          title="Pending Repairs"
          value={String(kpis?.pendingRepairs ?? 0)}
          icon={Wrench}
        />
        <KPICard
          title="Orders This Week"
          value={String(kpis?.orders7d ?? 0)}
          icon={ShoppingCart}
        />
      </div>

      <div className="grid lg:grid-cols-2 gap-6">
        {/* Recent Orders */}
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Recent Orders</CardTitle>
            <CardDescription>Latest orders across all channels</CardDescription>
          </CardHeader>
          <CardContent>
            {dashboard?.recentOrders.length === 0 ? (
              <p className="text-sm text-muted-foreground text-center py-8">No orders yet</p>
            ) : (
              <div className="space-y-3">
                {dashboard?.recentOrders.map((order) => (
                  <div key={order.id} className="flex items-center justify-between py-2 border-b last:border-0">
                    <div>
                      <p className="text-sm font-medium">{order.orderNumber}</p>
                      <p className="text-xs text-muted-foreground">
                        {order.user.firstName} {order.user.lastName} &middot; {formatDate(order.createdAt)}
                      </p>
                    </div>
                    <div className="text-right">
                      <p className="text-sm font-bold">{formatCurrency(Number(order.totalAmount))}</p>
                      <Badge variant={statusColors[order.status] as any ?? "secondary"} className="text-[10px]">
                        {order.status}
                      </Badge>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        {/* Channel Breakdown */}
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Channel Performance</CardTitle>
            <CardDescription>Revenue by sales channel (30d)</CardDescription>
          </CardHeader>
          <CardContent>
            {dashboard?.channelBreakdown.length === 0 ? (
              <p className="text-sm text-muted-foreground text-center py-8">No channel data yet</p>
            ) : (
              <div className="space-y-4">
                {dashboard?.channelBreakdown.map((ch) => {
                  const revenue = Number(ch._sum.totalAmount ?? 0);
                  const totalRevenue = Number(kpis?.totalRevenue30d ?? 1);
                  const pct = totalRevenue > 0 ? (revenue / totalRevenue) * 100 : 0;
                  return (
                    <div key={ch.channel}>
                      <div className="flex justify-between text-sm mb-1">
                        <span className="font-medium">{ch.channel.replace(/_/g, " ")}</span>
                        <span>{formatCurrency(revenue)} ({ch._count} orders)</span>
                      </div>
                      <div className="h-2 bg-muted rounded-full overflow-hidden">
                        <div
                          className="h-full bg-brand-500 rounded-full transition-all"
                          style={{ width: `${Math.min(pct, 100)}%` }}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
