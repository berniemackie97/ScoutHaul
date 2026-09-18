import type { components } from "@overhaul/api-client";

export type ReportDetection = components["schemas"]["ReportDetectionDto"];
export type SyncRun = components["schemas"]["SyncRunDto"];
export type SyncRunIssue = components["schemas"]["SyncRunIssueDto"];

/** What the file picker offers. Detection decides what a file really is; this only filters the dialog. */
export const ACCEPTED_FILES = ".csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

/** The exports the importer reads, as the operator would describe where to get them. */
export const SUPPORTED_EXPORTS: { platform: string; name: string; where: string }[] = [
  { platform: "eBay", name: "All Orders report", where: "Seller Hub → Orders → Download report" },
  { platform: "eBay", name: "Listings Sales report", where: "Seller Hub → Performance → Sales" },
  { platform: "eBay", name: "Active Listings report", where: "Seller Hub → Listings → Active → Download" },
  { platform: "eBay", name: "Listing Quality workbook", where: "Seller Hub → Performance → Listing quality" },
  { platform: "Depop", name: "Sales export", where: "depop.com → Settings → Sales → Download" },
  { platform: "Poshmark", name: "Inventory report", where: "poshmark.com → My Closet → Inventory report" },
];

export type Tone = "success" | "warning" | "danger" | "neutral" | "info";

/** A run's status in the operator's words, with the colour that goes with it. */
export function runStatus(status: string | null | undefined): { label: string; tone: Tone } {
  switch (status) {
    case "Succeeded":
      return { label: "Imported", tone: "success" };
    case "CompletedWithIssues":
      return { label: "Imported with notes", tone: "warning" };
    case "Failed":
      return { label: "Failed", tone: "danger" };
    case "Running":
      return { label: "Running", tone: "info" };
    default:
      return { label: status ?? "Unknown", tone: "neutral" };
  }
}

/** "3 new · 12 updated · 40 unchanged · 1 rejected" — only the parts that happened. */
export function countsSentence(run: Pick<SyncRun, "created" | "updated" | "unchanged" | "rejected">): string {
  const parts = [
    [run.created, "new"],
    [run.updated, "updated"],
    [run.unchanged, "unchanged"],
    [run.rejected, "rejected"],
  ] as const;
  const shown = parts.filter(([count]) => (count ?? 0) > 0).map(([count, label]) => `${count} ${label}`);
  return shown.length > 0 ? shown.join(" · ") : "Nothing to import";
}

const DAY = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
const DAY_NO_YEAR = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", timeZone: "UTC" });

/**
 * What a file covers: "Jul 1 – Jul 31, 2026" for a period, "as of Jul 9, 2026" for a snapshot.
 * Formatted in UTC because that is how the server sends these instants; a local conversion would
 * move a midnight to the day before.
 */
export function coverage(
  from: string | null | undefined,
  to: string | null | undefined,
  snapshot: boolean,
): string | null {
  if (snapshot) return to ? `as of ${DAY.format(new Date(to))}` : null;
  if (!from && !to) return null;
  if (from && to) {
    const start = new Date(from);
    const end = new Date(to);
    if (DAY.format(start) === DAY.format(end)) return DAY.format(end);
    const sameYear = start.getUTCFullYear() === end.getUTCFullYear();
    return `${sameYear ? DAY_NO_YEAR.format(start) : DAY.format(start)} – ${DAY.format(end)}`;
  }
  return DAY.format(new Date((from ?? to)!));
}

/** What a detected file holds: "58 orders", "1 listing". */
export function recordsLabel(detection: Pick<ReportDetection, "records" | "formatKey">): string {
  const count = detection.records ?? 0;
  const noun = recordNoun(detection.formatKey);
  return `${count} ${count === 1 ? noun : `${noun}s`}`;
}

function recordNoun(formatKey: string | null | undefined): string {
  if (!formatKey) return "row";
  if (formatKey.endsWith("listing-quality")) return "quality row";
  if (formatKey.includes("listing")) return formatKey.endsWith("listing-sales") ? "sale" : "listing";
  if (formatKey.endsWith("inventory")) return "listing";
  return "order";
}

export function fileSize(bytes: number | null | undefined): string {
  const value = bytes ?? 0;
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(value < 10 * 1024 ? 1 : 0)} KB`;
  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

/** The detections that will actually be imported when the operator confirms. */
export function importable(detections: ReportDetection[]): ReportDetection[] {
  return detections.filter((detection) => detection.canImport);
}

export function issueCounts(issues: SyncRunIssue[] | null | undefined): { errors: number; warnings: number } {
  const list = issues ?? [];
  return {
    errors: list.filter((issue) => issue.severity === "Error").length,
    warnings: list.filter((issue) => issue.severity !== "Error").length,
  };
}
