"use client";

import { useState } from "react";
import { Wrench, Clock, CheckCircle, AlertCircle } from "lucide-react";
import { Button, Card, Badge, formatCurrency, formatDate } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

const statusColors: Record<string, string> = {
  SUBMITTED: "secondary", RECEIVED: "info", DIAGNOSING: "info",
  QUOTE_SENT: "warning", APPROVED: "success", IN_PROGRESS: "default",
  COMPLETED: "success", READY_FOR_PICKUP: "success", PICKED_UP: "default",
  CANCELLED: "destructive",
};

const priorityColors: Record<string, string> = {
  LOW: "secondary", NORMAL: "default", HIGH: "warning", RUSH: "destructive",
};

export default function RepairsPage() {
  const [statusFilter, setStatusFilter] = useState<string | undefined>(undefined);
  const { data: repairs, isLoading } = trpc.repairs.listAll.useQuery({
    status: statusFilter,
  });

  const activeCount = repairs?.filter((r) =>
    ["SUBMITTED", "RECEIVED", "DIAGNOSING", "IN_PROGRESS", "APPROVED"].includes(r.status)
  ).length ?? 0;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">Repair Jobs</h1>
          <p className="text-sm text-muted-foreground">{activeCount} active jobs</p>
        </div>
      </div>

      {/* Quick stats */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-6">
        {[
          { label: "Submitted", status: "SUBMITTED", icon: AlertCircle, color: "text-yellow-600" },
          { label: "In Progress", status: "IN_PROGRESS", icon: Wrench, color: "text-blue-600" },
          { label: "Completed", status: "COMPLETED", icon: CheckCircle, color: "text-green-600" },
          { label: "Ready for Pickup", status: "READY_FOR_PICKUP", icon: Clock, color: "text-purple-600" },
        ].map((stat) => (
          <Card
            key={stat.status}
            className="p-4 cursor-pointer hover:shadow-md transition-shadow"
            onClick={() => setStatusFilter(statusFilter === stat.status ? undefined : stat.status)}
          >
            <stat.icon className={`h-5 w-5 ${stat.color} mb-2`} />
            <p className="text-2xl font-bold">
              {repairs?.filter((r) => r.status === stat.status).length ?? 0}
            </p>
            <p className="text-xs text-muted-foreground">{stat.label}</p>
          </Card>
        ))}
      </div>

      <Card>
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b bg-muted/50">
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Job #</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Customer</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Item</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Status</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Priority</th>
                <th className="text-right text-xs font-medium text-muted-foreground p-3">Est. Cost</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Submitted</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                Array.from({ length: 3 }).map((_, i) => (
                  <tr key={i} className="border-b"><td colSpan={7} className="p-3"><div className="h-8 bg-muted rounded animate-pulse" /></td></tr>
                ))
              ) : repairs?.length === 0 ? (
                <tr><td colSpan={7} className="p-8 text-center text-muted-foreground">No repair jobs found</td></tr>
              ) : (
                repairs?.map((job) => (
                  <tr key={job.id} className="border-b hover:bg-muted/30">
                    <td className="p-3 text-sm font-mono">{job.jobNumber}</td>
                    <td className="p-3">
                      <p className="text-sm">{job.user.firstName} {job.user.lastName}</p>
                      <p className="text-xs text-muted-foreground">{job.user.email}</p>
                    </td>
                    <td className="p-3">
                      <p className="text-sm">{job.itemDescription}</p>
                      <p className="text-xs text-muted-foreground">{job.itemBrand} {job.itemModel}</p>
                    </td>
                    <td className="p-3"><Badge variant={statusColors[job.status] as any}>{job.status.replace(/_/g, " ")}</Badge></td>
                    <td className="p-3"><Badge variant={priorityColors[job.priority] as any}>{job.priority}</Badge></td>
                    <td className="p-3 text-right text-sm">
                      {job.estimatedCost ? formatCurrency(Number(job.estimatedCost)) : "-"}
                    </td>
                    <td className="p-3 text-sm text-muted-foreground">{formatDate(job.createdAt)}</td>
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
