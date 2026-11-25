import { CheckCircle2, CircleAlert, CircleDashed } from "lucide-react";
import type { InventoryStatus } from "./inventory-operations";
import { readinessRequirements, type InventoryReadinessFacts } from "./lifecycle-readiness";

export function ReadinessChecklist({
  targetStatus,
  facts,
  compact = false,
}: {
  targetStatus: InventoryStatus;
  facts: InventoryReadinessFacts;
  compact?: boolean;
}) {
  const requirements = readinessRequirements(targetStatus, facts);
  const blockers = requirements.filter((requirement) => requirement.severity === "blocker" && !requirement.met);

  if (requirements.length === 0) return null;

  return (
    <div className="rounded-lg border bg-muted/20 p-3 text-sm">
      <div className="mb-2 flex items-center justify-between gap-3">
        <p className="font-medium">Readiness requirements</p>
        {blockers.length > 0 ? (
          <span className="text-destructive text-xs">{blockers.length} blocker{blockers.length === 1 ? "" : "s"}</span>
        ) : (
          <span className="text-success text-xs">No blockers</span>
        )}
      </div>
      <ul className={compact ? "space-y-1" : "space-y-2"}>
        {requirements.map((requirement) => {
          const Icon = requirement.met ? CheckCircle2 : requirement.severity === "blocker" ? CircleAlert : CircleDashed;
          return (
            <li key={requirement.id} className="flex items-start gap-2">
              <Icon
                className={`mt-0.5 h-4 w-4 shrink-0 ${
                  requirement.met ? "text-success" : requirement.severity === "blocker" ? "text-destructive" : "text-warning"
                }`}
              />
              <span className={requirement.met ? "text-muted-foreground" : ""}>{requirement.label}</span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
