import Link from "next/link";
import {
  ArrowDownRight,
  ArrowRight,
  ArrowUpRight,
  Boxes,
  CheckCircle2,
  CircleDollarSign,
  Inbox,
  Minus,
  Receipt,
  TrendingUp,
  Wallet,
  type LucideIcon,
} from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Card, formatCurrency } from "@overhaul/ui";
import { LocalDate } from "@/components/local-date";
import type { Operator } from "@/lib/session";
import { changeTone, describeChange, type OperatorConsole } from "./trading";

type ReportChange = components["schemas"]["ReportChangeDto"];
type WorkInbox = components["schemas"]["WorkInboxDto"];

// Whole dollars on the tiles, and $0 rather than a dash: an aggregate of nothing really is nought.
const money = (value: number | null | undefined) => formatCurrency(value ?? 0, { maximumFractionDigits: 0 });

/**
 * The first screen of the day: how much work is waiting, where, and how trading is going. The work itself lives in
 * Work — this page only points at its lanes, so no queue is counted or linked in two places.
 */
export function CommandCenter({ overview, work, operator }: { overview: OperatorConsole; work: WorkInbox | null; operator: Operator | null }) {
  return (
    <div className="space-y-8">
      <header className="flex flex-col justify-between gap-2 sm:flex-row sm:items-end">
        <div>
          <p className="text-muted-foreground text-sm">{operator?.organization ?? "Operator workspace"}</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Command center</h1>
          {work ? <p className="text-muted-foreground mt-1 text-sm">{headline(work)}</p> : null}
        </div>
        <LocalDate className="text-muted-foreground text-xs" />
      </header>

      {work ? <WorkSummary work={work} /> : null}

      {overview.money || overview.inventory ? (
        <div className="grid gap-6 lg:grid-cols-2">
          {overview.money ? <TradingCard money={overview.money} /> : null}
          {overview.inventory ? <StockCard inventory={overview.inventory} /> : null}
        </div>
      ) : null}
    </div>
  );
}

function headline(work: WorkInbox): string {
  const open = work.openCount ?? 0;
  if (open === 0) return "Nothing waiting on you right now.";
  const late = work.lateCount ?? 0;
  return `${open} open${late > 0 ? ` · ${late} past due` : ""} · ${work.inTransit ?? 0} with a carrier.`;
}

/** One tile per lane of Work with its count, how many are late, and the first thing in it. */
function WorkSummary({ work }: { work: WorkInbox }) {
  const lanes = work.lanes ?? [];
  const busy = lanes.filter((lane) => (lane.tasks?.length ?? 0) > 0);
  return (
    <section aria-labelledby="work-heading" className="space-y-3">
      <div className="flex items-center justify-between gap-3">
        <h2 id="work-heading" className="flex items-center gap-2 text-base font-semibold">
          <Inbox className="text-muted-foreground h-4 w-4" aria-hidden /> Work
        </h2>
        <Link href="/work" className="text-copper inline-flex items-center gap-1 text-sm font-medium hover:underline">
          Open Work <ArrowRight className="h-3.5 w-3.5" aria-hidden />
        </Link>
      </div>
      {busy.length === 0 ? (
        <Card className="flex items-center gap-3 p-5 text-sm">
          <CheckCircle2 className="text-success h-5 w-5" aria-hidden /> Every lane is clear.
        </Card>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {busy.map((lane) => {
            const tasks = lane.tasks ?? [];
            const count = tasks.reduce((total, task) => total + (task.count ?? 1), 0);
            const late = tasks.filter((task) => task.isLate).length;
            return (
              <Link key={lane.key} href={`/work?lane=${lane.key}`} className="group">
                <Card className={`h-full p-5 transition-colors group-hover:border-copper/50 ${late > 0 ? "border-warning-border" : ""}`}>
                  <div className="flex items-baseline justify-between gap-3">
                    <span className="font-semibold">{lane.label}</span>
                    <span className="text-2xl font-semibold tabular-nums">{count}</span>
                  </div>
                  <p className="text-muted-foreground mt-1 text-xs">{lane.hint}</p>
                  {late > 0 && <p className="text-destructive mt-2 text-xs font-medium">{late} past due</p>}
                  <p className="mt-3 truncate text-sm">{tasks[0]?.title}</p>
                </Card>
              </Link>
            );
          })}
        </div>
      )}
    </section>
  );
}

function TradingCard({ money: trading }: { money: NonNullable<OperatorConsole["money"]> }) {
  const comparison = trading.comparison;
  const reportHref = `/analytics?from=${trading.from}&to=${trading.to}`;
  const rows: { label: string; value: string; change: ReportChange | undefined; icon: LucideIcon }[] = [
    { label: "Gross sales", value: money(trading.current?.grossSales), change: comparison?.grossSales, icon: CircleDollarSign },
    { label: "Net profit", value: money(trading.current?.realizedNetProfit), change: comparison?.netProfit, icon: TrendingUp },
    { label: "Sales", value: String(trading.current?.salesCount ?? 0), change: comparison?.salesCount, icon: Receipt },
    { label: "Average sale", value: money(trading.current?.averageSaleValue), change: comparison?.averageSaleValue, icon: Wallet },
  ];

  return (
    <Card className="p-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h2 className="text-sm font-semibold">Last {trading.days} days</h2>
          <p className="text-muted-foreground mt-1 text-xs">Compared with the {trading.days} days before.</p>
        </div>
        <Link href={reportHref} className="text-brand-600 inline-flex shrink-0 items-center gap-1 whitespace-nowrap text-xs font-medium hover:underline">
          Open report <ArrowRight className="h-3.5 w-3.5" aria-hidden />
        </Link>
      </div>
      <dl className="mt-5 grid grid-cols-2 gap-5">
        {rows.map((row) => (
          <div key={row.label}>
            <dt className="text-muted-foreground flex items-center gap-1.5 text-xs uppercase tracking-wide">
              <row.icon className="h-3.5 w-3.5" aria-hidden /> {row.label}
            </dt>
            <dd className="mt-1 text-xl font-semibold tabular-nums">{row.value}</dd>
            <dd className="mt-0.5">
              <Change change={row.change} />
            </dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}

function Change({ change }: { change: ReportChange | undefined }) {
  const tone = changeTone(change);
  const Icon = tone === "up" ? ArrowUpRight : tone === "down" ? ArrowDownRight : Minus;
  const color = tone === "up" ? "text-success" : tone === "down" ? "text-destructive" : "text-muted-foreground";
  return (
    <span className={`inline-flex items-center gap-0.5 text-xs font-medium ${color}`}>
      <Icon className="h-3.5 w-3.5" aria-hidden /> {describeChange(change)}
    </span>
  );
}

function StockCard({ inventory }: { inventory: NonNullable<OperatorConsole["inventory"]> }) {
  const spread = (inventory.estimatedResaleValue ?? 0) - (inventory.capitalDeployed ?? 0);
  return (
    <Card className="p-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h2 className="text-sm font-semibold">Stock on hand</h2>
          <p className="text-muted-foreground mt-1 text-xs">Everything not yet sold — the same figures as the inventory page.</p>
        </div>
        <Link href="/inventory?lifecycle=Active" className="text-brand-600 inline-flex shrink-0 items-center gap-1 whitespace-nowrap text-xs font-medium hover:underline">
          Open inventory <ArrowRight className="h-3.5 w-3.5" aria-hidden />
        </Link>
      </div>
      <dl className="mt-5 grid grid-cols-2 gap-5">
        <Figure label="Items" value={String(inventory.activeItems ?? 0)} icon={Boxes} />
        <Figure label="Capital deployed" value={money(inventory.capitalDeployed)} icon={Wallet} />
        <Figure label="Expected value" value={money(inventory.estimatedResaleValue)} icon={TrendingUp} />
        <Figure label="Projected spread" value={money(spread)} icon={CircleDollarSign} />
      </dl>
    </Card>
  );
}

function Figure({ label, value, icon: Icon }: { label: string; value: string; icon: LucideIcon }) {
  return (
    <div>
      <dt className="text-muted-foreground flex items-center gap-1.5 text-xs uppercase tracking-wide">
        <Icon className="h-3.5 w-3.5" aria-hidden /> {label}
      </dt>
      <dd className="mt-1 text-xl font-semibold tabular-nums">{value}</dd>
    </div>
  );
}
