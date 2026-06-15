"use client";

import Link from "next/link";
import { ArrowLeft, Package, ExternalLink } from "lucide-react";
import {
  Card, CardContent, Badge, Separator,
} from "@overhaul/ui";
import { formatCurrency, formatDate } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

const statusColors: Record<string, string> = {
  PENDING: "warning",
  CONFIRMED: "info",
  PROCESSING: "info",
  SHIPPED: "default",
  DELIVERED: "success",
  CANCELLED: "destructive",
  REFUNDED: "destructive",
};

export default function OrdersPage() {
  const { data: orders, isLoading } = trpc.orders.myOrders.useQuery();

  return (
    <div className="container mx-auto px-4 py-8 max-w-3xl">
      <Link href="/account" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mb-6">
        <ArrowLeft className="h-4 w-4" /> Account
      </Link>

      <h1 className="text-3xl font-bold mb-8">My Orders</h1>

      {isLoading ? (
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-32 bg-muted rounded-xl animate-pulse" />
          ))}
        </div>
      ) : orders?.length === 0 ? (
        <div className="text-center py-16">
          <Package className="h-12 w-12 mx-auto mb-4 text-muted-foreground" />
          <h2 className="text-xl font-medium mb-2">No orders yet</h2>
          <p className="text-muted-foreground mb-6">When you place an order, it will appear here.</p>
          <Link href="/products" className="text-brand-600 hover:underline">Start shopping</Link>
        </div>
      ) : (
        <div className="space-y-4">
          {orders?.map((order) => (
            <Card key={order.id}>
              <CardContent className="p-6">
                <div className="flex items-start justify-between mb-4">
                  <div>
                    <p className="font-mono text-sm text-muted-foreground">Order #{order.orderNumber}</p>
                    <p className="text-sm text-muted-foreground">{formatDate(order.createdAt)}</p>
                  </div>
                  <Badge variant={statusColors[order.status] as any ?? "default"}>
                    {order.status.replace("_", " ")}
                  </Badge>
                </div>
                <Separator className="mb-4" />
                <div className="space-y-2">
                  {order.items.map((item) => (
                    <div key={item.id} className="flex justify-between text-sm">
                      <span>{item.product.name} x{item.quantity}</span>
                      <span>{formatCurrency(Number(item.unitPrice) * item.quantity)}</span>
                    </div>
                  ))}
                </div>
                <Separator className="my-4" />
                <div className="flex justify-between font-medium">
                  <span>Total</span>
                  <span>{formatCurrency(Number(order.total))}</span>
                </div>
                {order.trackingNumber && (
                  <p className="text-sm text-muted-foreground mt-3 flex items-center gap-1">
                    Tracking: {order.trackingNumber} <ExternalLink className="h-3 w-3" />
                  </p>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
