"use client";

import Link from "next/link";
import { ArrowLeft, Wrench } from "lucide-react";
import { Card, CardContent, Badge, Separator, Button } from "@overhaul/ui";
import { formatCurrency, formatDate } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

const statusColors: Record<string, string> = {
  SUBMITTED: "secondary",
  QUOTED: "warning",
  APPROVED: "info",
  IN_PROGRESS: "info",
  COMPLETED: "success",
  READY_FOR_PICKUP: "success",
  PICKED_UP: "default",
  CANCELLED: "destructive",
};

export default function MyRepairsPage() {
  const { data: repairs, isLoading } = trpc.repairs.myJobs.useQuery();

  return (
    <div className="container mx-auto px-4 py-8 max-w-3xl">
      <Link href="/account" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mb-6">
        <ArrowLeft className="h-4 w-4" /> Account
      </Link>

      <div className="flex items-center justify-between mb-8">
        <h1 className="text-3xl font-bold">My Repairs</h1>
        <Link href="/repairs">
          <Button variant="outline" size="sm">
            <Wrench className="h-4 w-4 mr-1" /> Request Repair
          </Button>
        </Link>
      </div>

      {isLoading ? (
        <div className="space-y-4">
          {Array.from({ length: 2 }).map((_, i) => (
            <div key={i} className="h-40 bg-muted rounded-xl animate-pulse" />
          ))}
        </div>
      ) : repairs?.length === 0 ? (
        <div className="text-center py-16">
          <Wrench className="h-12 w-12 mx-auto mb-4 text-muted-foreground" />
          <h2 className="text-xl font-medium mb-2">No repair requests</h2>
          <p className="text-muted-foreground mb-6">Need something fixed? Submit a repair request.</p>
          <Link href="/repairs" className="text-brand-600 hover:underline">Request a repair</Link>
        </div>
      ) : (
        <div className="space-y-4">
          {repairs?.map((job) => (
            <Card key={job.id}>
              <CardContent className="p-6">
                <div className="flex items-start justify-between mb-3">
                  <div>
                    <p className="font-mono text-sm text-muted-foreground">Job #{job.jobNumber}</p>
                    <h3 className="font-medium mt-1">{job.itemDescription}</h3>
                  </div>
                  <Badge variant={statusColors[job.status] as any ?? "default"}>
                    {job.status.replace(/_/g, " ")}
                  </Badge>
                </div>
                <p className="text-sm text-muted-foreground mb-4">{job.issueDescription}</p>
                <Separator className="mb-3" />
                <div className="flex gap-6 text-sm">
                  <span>Submitted: {formatDate(job.createdAt)}</span>
                  {job.estimatedCost && (
                    <span>Estimated: {formatCurrency(Number(job.estimatedCost))}</span>
                  )}
                  {job.priority && (
                    <Badge variant={job.priority === "URGENT" ? "destructive" : "outline"} className="text-xs">
                      {job.priority}
                    </Badge>
                  )}
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
