"use client";

import type { components } from "@overhaul/api-client";
import { formatCurrency } from "@overhaul/ui";
import { Area, CartesianGrid, ComposedChart, Line, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { compactMoney } from "@/features/reports/report-format";

type Point = components["schemas"]["ReportSeriesPointDto"];

const SERIES = [
  { key: "netProfit", label: "Net profit", color: "var(--viz-1)" },
  { key: "grossSales", label: "Gross sales", color: "var(--viz-context)" },
] as const;

/** Gross sales is context (a gray wash); net profit is the point (the accent line). One money axis. */
export function TrendChart({ series }: { series: Point[] }) {
  if (series.length === 0) {
    return <div className="text-muted-foreground flex h-[300px] items-center justify-center rounded-lg border border-dashed text-sm">No sales in this period.</div>;
  }
  const hasLoss = series.some((point) => (point.netProfit ?? 0) < 0);
  return (
    <div className="h-[300px] w-full">
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={series} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
          <CartesianGrid vertical={false} stroke="var(--viz-grid)" />
          <XAxis
            dataKey="label"
            tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }}
            tickLine={false}
            axisLine={{ stroke: "var(--viz-grid)" }}
            minTickGap={28}
          />
          <YAxis
            tickFormatter={compactMoney}
            tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }}
            tickLine={false}
            axisLine={false}
            width={56}
          />
          {hasLoss && <ReferenceLine y={0} stroke="hsl(var(--muted-foreground))" strokeOpacity={0.5} />}
          <Tooltip cursor={{ stroke: "hsl(var(--muted-foreground))", strokeWidth: 1 }} content={<TrendTooltip />} />
          <Area
            type="monotone"
            dataKey="grossSales"
            name="Gross sales"
            stroke="var(--viz-context)"
            strokeWidth={2}
            fill="var(--viz-context)"
            fillOpacity={0.1}
            dot={false}
            activeDot={{ r: 4, stroke: "hsl(var(--card))", strokeWidth: 2 }}
            isAnimationActive={false}
          />
          <Line
            type="monotone"
            dataKey="netProfit"
            name="Net profit"
            stroke="var(--viz-1)"
            strokeWidth={2}
            strokeLinecap="round"
            dot={false}
            activeDot={{ r: 4, stroke: "hsl(var(--card))", strokeWidth: 2 }}
            isAnimationActive={false}
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}

function TrendTooltip({ active, payload, label }: { active?: boolean; payload?: Array<{ payload: Point }>; label?: string }) {
  const point = payload?.[0]?.payload;
  if (!active || !point) return null;
  return (
    <div className="bg-popover text-popover-foreground min-w-[180px] rounded-lg border p-3 text-xs shadow-md">
      <p className="text-muted-foreground mb-2">{label}</p>
      {SERIES.map((series) => (
        <div key={series.key} className="flex items-center justify-between gap-4 py-0.5">
          <span className="text-muted-foreground flex items-center gap-1.5">
            <span className="h-0.5 w-3 rounded-full" style={{ background: series.color }} aria-hidden />
            {series.label}
          </span>
          <span className="text-sm font-semibold tabular-nums">{formatCurrency(point[series.key] ?? 0)}</span>
        </div>
      ))}
      <p className="text-muted-foreground mt-1.5 border-t pt-1.5">
        {point.salesCount ?? 0} sale{point.salesCount === 1 ? "" : "s"}
      </p>
    </div>
  );
}
