"use client";

import { Card, Badge, formatCurrency, formatDate } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

const statusColors: Record<string, string> = {
  SUBMITTED: "secondary", UNDER_REVIEW: "info", ACCEPTED: "success",
  REJECTED: "destructive", LISTED: "default", SOLD: "success",
  PAID_OUT: "success", EXPIRED: "warning", RETURNED: "warning",
};

export default function ConsignmentsPage() {
  const { data, isLoading } = trpc.consignments.listAll.useQuery({});

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-2xl font-bold">Consignments</h1>
        <p className="text-sm text-muted-foreground">Manage consignment submissions</p>
      </div>

      <Card>
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b bg-muted/50">
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Submission #</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Customer</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Item</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Condition</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Status</th>
                <th className="text-right text-xs font-medium text-muted-foreground p-3">Est. Value</th>
                <th className="text-right text-xs font-medium text-muted-foreground p-3">Commission</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Submitted</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                Array.from({ length: 3 }).map((_, i) => (
                  <tr key={i} className="border-b"><td colSpan={8} className="p-3"><div className="h-8 bg-muted rounded animate-pulse" /></td></tr>
                ))
              ) : data?.length === 0 ? (
                <tr><td colSpan={8} className="p-8 text-center text-muted-foreground">No consignment submissions yet</td></tr>
              ) : (
                data?.map((sub) => (
                  <tr key={sub.id} className="border-b hover:bg-muted/30">
                    <td className="p-3 text-sm font-mono">{sub.submissionNumber}</td>
                    <td className="p-3 text-sm">{sub.user.firstName} {sub.user.lastName}</td>
                    <td className="p-3 text-sm">{sub.itemDescription}</td>
                    <td className="p-3 text-xs">{sub.condition.replace(/_/g, " ")}</td>
                    <td className="p-3"><Badge variant={statusColors[sub.status] as any}>{sub.status.replace(/_/g, " ")}</Badge></td>
                    <td className="p-3 text-right text-sm">{sub.estimatedValue ? formatCurrency(Number(sub.estimatedValue)) : "-"}</td>
                    <td className="p-3 text-right text-sm">{Number(sub.commissionRate)}%</td>
                    <td className="p-3 text-sm text-muted-foreground">{formatDate(sub.createdAt)}</td>
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
