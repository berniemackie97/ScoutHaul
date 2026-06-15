"use client";

import { useState } from "react";
import { Search, Users, Mail, ShoppingBag, Wrench } from "lucide-react";
import { Button, Input, Card, Badge, formatDate } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

export default function CustomersPage() {
  const [search, setSearch] = useState("");
  const { data, isLoading } = trpc.customers.listAll.useQuery({ search: search || undefined, limit: 50 });

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">Customers</h1>
          <p className="text-sm text-muted-foreground">Customer relationship management</p>
        </div>
      </div>

      <div className="flex gap-3 mb-6">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input placeholder="Search by name or email..." className="pl-10" value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>
      </div>

      <Card>
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b bg-muted/50">
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Customer</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Email</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Joined</th>
                <th className="text-center text-xs font-medium text-muted-foreground p-3">Orders</th>
                <th className="text-center text-xs font-medium text-muted-foreground p-3">Repairs</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                Array.from({ length: 5 }).map((_, i) => (
                  <tr key={i} className="border-b"><td colSpan={5} className="p-3"><div className="h-8 bg-muted rounded animate-pulse" /></td></tr>
                ))
              ) : data?.items.length === 0 ? (
                <tr><td colSpan={5} className="p-8 text-center text-muted-foreground">No customers found</td></tr>
              ) : (
                data?.items.map((customer) => (
                  <tr key={customer.id} className="border-b hover:bg-muted/30">
                    <td className="p-3">
                      <div className="flex items-center gap-3">
                        <div className="h-9 w-9 rounded-full bg-brand-100 flex items-center justify-center text-brand-600 font-semibold text-sm">
                          {(customer.firstName?.[0] ?? "?")}{(customer.lastName?.[0] ?? "")}
                        </div>
                        <div>
                          <p className="text-sm font-medium">{customer.firstName} {customer.lastName}</p>
                          <p className="text-xs text-muted-foreground">{customer.phone ?? "No phone"}</p>
                        </div>
                      </div>
                    </td>
                    <td className="p-3 text-sm">{customer.email}</td>
                    <td className="p-3 text-sm text-muted-foreground">{formatDate(customer.createdAt)}</td>
                    <td className="p-3 text-center">
                      <Badge variant="outline">{customer._count.orders}</Badge>
                    </td>
                    <td className="p-3 text-center">
                      <Badge variant="outline">{customer._count.repairJobs}</Badge>
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
