"use client";

import type { components } from "@overhaul/api-client";
import { formatCurrency } from "@overhaul/ui";
import {
  Area,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  ComposedChart,
  Legend,
  Line,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

type BusinessReport = components["schemas"]["BusinessReportDto"];

const colors = ["#2563eb", "#16a34a", "#f59e0b", "#7c3aed", "#dc2626", "#0891b2", "#64748b"];
const currency = (value: number) => formatCurrency(value, { maximumFractionDigits: 0 });
const compact = (value: number) => new Intl.NumberFormat("en-US", { notation: "compact", maximumFractionDigits: 1 }).format(value);

export function PerformanceChart({ series }: { series: NonNullable<BusinessReport["series"]> }) {
  if (series.length === 0) return <EmptyChart />;
  return (
    <div className="h-[340px] w-full">
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={series} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
          <defs>
            <linearGradient id="salesFill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#2563eb" stopOpacity={0.28} />
              <stop offset="95%" stopColor="#2563eb" stopOpacity={0.02} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" vertical={false} />
          <XAxis dataKey="label" tick={{ fontSize: 11 }} tickLine={false} axisLine={false} minTickGap={24} />
          <YAxis tickFormatter={compact} tick={{ fontSize: 11 }} tickLine={false} axisLine={false} width={48} />
          <Tooltip formatter={(value) => currency(Number(value))} contentStyle={{ borderRadius: 10, borderColor: "#e2e8f0" }} />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          <Area type="monotone" dataKey="grossSales" name="Gross sales" stroke="#2563eb" fill="url(#salesFill)" strokeWidth={2} />
          <Bar dataKey="operatingExpenses" name="Operating expenses" fill="#f59e0b" opacity={0.75} radius={[3, 3, 0, 0]} />
          <Line type="monotone" dataKey="netProfit" name="Realized net profit" stroke="#16a34a" strokeWidth={2.5} dot={false} />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}

export function PlatformChart({ data }: { data: NonNullable<BusinessReport["platforms"]> }) {
  if (data.length === 0) return <EmptyChart />;
  return (
    <div className="h-[300px] w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data.slice(0, 8)} layout="vertical" margin={{ left: 14, right: 16 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" horizontal={false} />
          <XAxis type="number" tickFormatter={compact} tick={{ fontSize: 11 }} tickLine={false} axisLine={false} />
          <YAxis type="category" dataKey="name" width={90} tick={{ fontSize: 11 }} tickLine={false} axisLine={false} />
          <Tooltip formatter={(value) => currency(Number(value))} contentStyle={{ borderRadius: 10, borderColor: "#e2e8f0" }} />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          <Bar dataKey="amount" name="Gross sales" fill="#2563eb" radius={[0, 4, 4, 0]} />
          <Bar dataKey="secondaryAmount" name="Net profit" fill="#16a34a" radius={[0, 4, 4, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

export function InventoryStatusChart({ data }: { data: NonNullable<BusinessReport["inventoryStatuses"]> }) {
  if (data.length === 0) return <EmptyChart />;
  return (
    <div className="h-[300px] w-full">
      <ResponsiveContainer width="100%" height="100%">
        <PieChart>
          <Pie data={data} dataKey="count" nameKey="name" innerRadius={62} outerRadius={98} paddingAngle={2}>
            {data.map((item, index) => <Cell key={item.name ?? index} fill={colors[index % colors.length]} />)}
          </Pie>
          <Tooltip formatter={(value) => `${Number(value)} items`} contentStyle={{ borderRadius: 10, borderColor: "#e2e8f0" }} />
          <Legend wrapperStyle={{ fontSize: 11 }} />
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
}

export function AgingChart({ data }: { data: NonNullable<BusinessReport["aging"]> }) {
  if (data.length === 0) return <EmptyChart />;
  return (
    <div className="h-[300px] w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" vertical={false} />
          <XAxis dataKey="label" tick={{ fontSize: 11 }} tickLine={false} axisLine={false} />
          <YAxis tickFormatter={compact} tick={{ fontSize: 11 }} tickLine={false} axisLine={false} width={48} />
          <Tooltip formatter={(value) => currency(Number(value))} contentStyle={{ borderRadius: 10, borderColor: "#e2e8f0" }} />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          <Bar dataKey="cost" name="Cost basis" fill="#7c3aed" radius={[4, 4, 0, 0]} />
          <Bar dataKey="estimatedValue" name="Expected value" fill="#0891b2" radius={[4, 4, 0, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

function EmptyChart() {
  return <div className="flex h-[280px] items-center justify-center rounded-lg border border-dashed text-sm text-muted-foreground">No data recorded for this period.</div>;
}
