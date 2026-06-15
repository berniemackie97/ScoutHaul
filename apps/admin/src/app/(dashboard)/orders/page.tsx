"use client";

import { useState } from "react";
import { Search, Filter, Eye, Truck, CheckCircle } from "lucide-react";
import {
  Button, Input, Card, Badge, formatCurrency, formatDate,
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
  Label,
} from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

const statusColors: Record<string, string> = {
  PENDING: "warning", CONFIRMED: "info", PROCESSING: "info",
  SHIPPED: "default", DELIVERED: "success", CANCELLED: "destructive",
  REFUNDED: "destructive", RETURNED: "warning",
};

export default function OrdersPage() {
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [selectedOrder, setSelectedOrder] = useState<string | null>(null);

  const { data, isLoading } = trpc.orders.listAll.useQuery({
    status: statusFilter !== "all" ? statusFilter : undefined,
    limit: 50,
  });

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">Orders</h1>
          <p className="text-sm text-muted-foreground">
            {data?.totalCount ?? 0} total orders
          </p>
        </div>
      </div>

      <div className="flex gap-3 mb-6">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input placeholder="Search orders..." className="pl-10" value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="w-[160px]"><SelectValue placeholder="All Status" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All Status</SelectItem>
            <SelectItem value="PENDING">Pending</SelectItem>
            <SelectItem value="CONFIRMED">Confirmed</SelectItem>
            <SelectItem value="PROCESSING">Processing</SelectItem>
            <SelectItem value="SHIPPED">Shipped</SelectItem>
            <SelectItem value="DELIVERED">Delivered</SelectItem>
            <SelectItem value="CANCELLED">Cancelled</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <Card>
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b bg-muted/50">
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Order</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Customer</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Date</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Channel</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Status</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Items</th>
                <th className="text-right text-xs font-medium text-muted-foreground p-3">Total</th>
                <th className="text-center text-xs font-medium text-muted-foreground p-3">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                Array.from({ length: 5 }).map((_, i) => (
                  <tr key={i} className="border-b"><td colSpan={8} className="p-3"><div className="h-8 bg-muted rounded animate-pulse" /></td></tr>
                ))
              ) : data?.items.length === 0 ? (
                <tr><td colSpan={8} className="p-8 text-center text-muted-foreground">No orders found</td></tr>
              ) : (
                data?.items.map((order) => (
                  <tr key={order.id} className="border-b hover:bg-muted/30">
                    <td className="p-3 text-sm font-mono font-medium">{order.orderNumber}</td>
                    <td className="p-3">
                      <p className="text-sm">{order.user.firstName} {order.user.lastName}</p>
                      <p className="text-xs text-muted-foreground">{order.user.email}</p>
                    </td>
                    <td className="p-3 text-sm text-muted-foreground">{formatDate(order.createdAt)}</td>
                    <td className="p-3"><Badge variant="outline" className="text-xs">{order.channel}</Badge></td>
                    <td className="p-3"><Badge variant={statusColors[order.status] as any}>{order.status}</Badge></td>
                    <td className="p-3 text-sm">{order.items.length} items</td>
                    <td className="p-3 text-right text-sm font-bold">{formatCurrency(Number(order.totalAmount))}</td>
                    <td className="p-3 text-center">
                      <Button variant="ghost" size="sm" onClick={() => setSelectedOrder(order.id)}>
                        <Eye className="h-4 w-4" />
                      </Button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  );
}
