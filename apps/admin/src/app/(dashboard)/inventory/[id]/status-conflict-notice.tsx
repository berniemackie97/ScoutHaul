import { AlertTriangle } from "lucide-react";
import { Card, CardContent } from "@overhaul/ui";
import type { StatusConflict } from "@/features/inventory/status-conflicts";

/**
 * Says out loud that the item's status and its marketplace listings disagree.
 *
 * The detail page has always rendered both — the status control and the channel panel — without
 * either knowing what the other said, so the operator had to spot the contradiction themselves.
 */
export function StatusConflictNotice({ conflicts }: { conflicts: StatusConflict[] }) {
  if (conflicts.length === 0) return null;

  return (
    <div className="space-y-3" role="alert">
      {conflicts.map((conflict) => {
        const danger = conflict.severity === "danger";
        return (
          <Card
            key={conflict.kind}
            className={danger ? "border-destructive/50 bg-destructive/5" : "border-yellow-500/50 bg-yellow-500/5"}
          >
            <CardContent className="flex gap-3 pt-6">
              <AlertTriangle
                className={`mt-0.5 h-5 w-5 shrink-0 ${danger ? "text-destructive" : "text-yellow-600"}`}
                aria-hidden
              />
              <div className="space-y-1">
                <p className="font-semibold">{conflict.title}</p>
                <p className="text-muted-foreground text-sm">{conflict.detail}</p>
                <p className="text-muted-foreground text-xs">
                  {conflict.channels.length === 1 ? "Channel: " : "Channels: "}
                  {conflict.channels.join(", ")}
                </p>
              </div>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}
