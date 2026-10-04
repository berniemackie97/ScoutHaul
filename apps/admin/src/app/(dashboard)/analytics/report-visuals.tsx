import { ArrowDownRight, ArrowRight, ArrowUpRight } from "lucide-react";
import { formatCurrency } from "@overhaul/ui";
import { changeTone, type BreakdownSegment } from "@/features/reports/report-format";

/** Signed change against the previous period, coloured by whether it is good news. */
export function Delta({
  change,
  upIsGood = true,
  points = false,
  className = "",
}: {
  change: number | null;
  upIsGood?: boolean;
  /** A change in percentage points (margins), not a percent change. */
  points?: boolean;
  className?: string;
}) {
  if (change == null) return <span className={`text-muted-foreground text-xs ${className}`}>No earlier period to compare</span>;
  const tone = changeTone(change, upIsGood);
  const Icon = Math.abs(change) < 0.05 ? ArrowRight : change > 0 ? ArrowUpRight : ArrowDownRight;
  const color = tone === "up" ? "text-[color:var(--viz-up)]" : tone === "down" ? "text-[color:var(--viz-down)]" : "text-muted-foreground";
  const amount = points ? `${Math.abs(change).toFixed(1)} pts` : `${Math.abs(change).toFixed(1)}%`;
  return (
    <span className={`inline-flex items-center gap-0.5 text-xs font-medium ${color} ${className}`}>
      <Icon className="h-3.5 w-3.5" aria-hidden />
      <span>
        {change > 0 ? "+" : change < 0 ? "−" : ""}
        {amount}
      </span>
      <span className="text-muted-foreground font-normal">&nbsp;vs previous</span>
    </span>
  );
}

/** A trend line in the context hue, its latest point in the accent. */
export function Sparkline({ values, label }: { values: number[]; label: string }) {
  if (values.length < 2) return null;
  const width = 120;
  const height = 32;
  const min = Math.min(...values, 0);
  const max = Math.max(...values, 0);
  const span = max - min || 1;
  const x = (index: number) => (index / (values.length - 1)) * (width - 6) + 3;
  const y = (value: number) => height - 3 - ((value - min) / span) * (height - 6);
  const points = values.map((value, index) => `${x(index).toFixed(1)},${y(value).toFixed(1)}`).join(" ");
  const last = values.length - 1;
  return (
    <svg viewBox={`0 0 ${width} ${height}`} className="h-8 w-[120px]" role="img" aria-label={label}>
      <polyline points={points} fill="none" stroke="var(--viz-context)" strokeWidth={1.5} strokeLinejoin="round" strokeLinecap="round" />
      <circle cx={x(last)} cy={y(values[last]!)} r={3} fill="var(--viz-1)" stroke="hsl(var(--card))" strokeWidth={1.5} />
    </svg>
  );
}

/** Part-to-whole: one bar, 2px surface gaps between segments, then a labelled legend that is the table view. */
export function BreakdownBar({ segments, total }: { segments: BreakdownSegment[]; total: number }) {
  if (total <= 0 || segments.length === 0) {
    return <p className="text-muted-foreground text-sm">No completed sales in this period.</p>;
  }
  return (
    <div className="space-y-4">
      <div className="flex h-4 w-full gap-[2px] overflow-hidden rounded-[4px]" role="img" aria-label="Where gross sales went">
        {segments.map((segment) => (
          <div
            key={segment.key}
            className="h-full first:rounded-l-[4px] last:rounded-r-[4px]"
            style={{ width: `${(segment.value / total) * 100}%`, background: segment.color, minWidth: 3 }}
            title={`${segment.label}: ${formatCurrency(segment.value)}`}
          />
        ))}
      </div>
      <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
        {segments.map((segment) => (
          <div key={segment.key} className="flex items-center justify-between gap-3">
            <dt className="text-muted-foreground flex min-w-0 items-center gap-2">
              <span className="h-2.5 w-2.5 shrink-0 rounded-[2px]" style={{ background: segment.color }} aria-hidden />
              <span className="truncate">{segment.label}</span>
            </dt>
            <dd className="whitespace-nowrap tabular-nums">
              <span className="font-medium">{formatCurrency(segment.value, { maximumFractionDigits: 0 })}</span>
              <span className="text-muted-foreground ml-2 inline-block w-10 text-right text-xs">
                {Math.round((segment.value / total) * 100)}%
              </span>
            </dd>
          </div>
        ))}
      </dl>
    </div>
  );
}

/** A horizontal bar inside a list row: thin, rounded at its data end, square at the baseline. */
export function RowBar({ ratio, color, track }: { ratio: number; color: string; track?: number }) {
  const clamp = (value: number) => Math.max(0, Math.min(1, value));
  return (
    <div className="relative h-2.5 w-full">
      {track != null && (
        <div
          className="absolute inset-y-0 left-0 rounded-r-[4px]"
          style={{ width: `${clamp(track) * 100}%`, background: "var(--viz-context)", opacity: 0.28 }}
        />
      )}
      <div
        className="absolute inset-y-0 left-0 rounded-r-[4px]"
        style={{ width: `${clamp(ratio) * 100}%`, background: color, minWidth: ratio > 0 ? 3 : 0 }}
      />
    </div>
  );
}

export function LegendKey({ color, label, line = false }: { color: string; label: string; line?: boolean }) {
  return (
    <span className="text-muted-foreground inline-flex items-center gap-1.5 text-xs">
      <span
        className={line ? "h-0.5 w-3.5 rounded-full" : "h-2.5 w-2.5 rounded-[2px]"}
        style={{ background: color }}
        aria-hidden
      />
      {label}
    </span>
  );
}
