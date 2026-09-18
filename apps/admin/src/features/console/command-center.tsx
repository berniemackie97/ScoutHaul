import Link from "next/link";
import {
  AlertTriangle,
  ArrowRight,
  ArrowDownRight,
  ArrowUpRight,
  Boxes,
  CheckCircle2,
  CircleDollarSign,
  ClipboardList,
  Minus,
  Package,
  PackageCheck,
  Receipt,
  Sparkles,
  TrendingUp,
  Wallet,
  Wrench,
  type LucideIcon,
} from "lucide-react";
import { Card, formatCurrency } from "@overhaul/ui";
import {
  changeTone,
  describeChange,
  presentQueues,
  type OperatorConsole,
  type PresentedQueue,
} from "./queues";
import { LocalDate } from "@/components/local-date";
import type { Operator } from "@/lib/session";
import type { components } from "@overhaul/api-client";

type ReportChange = components["schemas"]["ReportChangeDto"];

// Whole dollars on the tiles, and $0 rather than a dash: an aggregate of nothing really is nought.
const money = (value: number | null | undefined) => formatCurrency(value ?? 0, { maximumFractionDigits: 0 });

const QUEUE_ICONS: Record<string, LucideIcon> = {
  "orders.toPack": Package,
  "orders.toShip": PackageCheck,
  "orders.readyForPickup": PackageCheck,
  "orders.awaitingPayment": Wallet,
  "inventory.readyToList": Sparkles,
  "inventory.benchWork": Wrench,
};

/**
 * The first screen of the day: what is waiting, how trading is going, and what the stock is worth.
 *
 * Pure presentation — the page fetches, this renders — so it can be exercised with any console the
 * API might return, including the partial ones a narrower role receives.
 *
 * What a caller may not open is absent, not zero: a packer's home page is the packing queue.
 */
export function CommandCenter({ overview, operator }: { overview: OperatorConsole; operator: Operator | null }) {
  const { byTier, clear } = presentQueues(overview.queues);
  const attentionCount = byTier.attention.reduce((sum, queue) => sum + (queue.count ?? 0), 0);
  const workCount = byTier.work.reduce((sum, queue) => sum + (queue.count ?? 0), 0);
  const hasQueues = (overview.queues?.length ?? 0) > 0;

  return (
    <div className="space-y-8">
      <header className="flex flex-col justify-between gap-2 sm:flex-row sm:items-end">
        <div>
          <p className="text-muted-foreground text-sm">{operator?.organization ?? "Operator workspace"}</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Command center</h1>
          {hasQueues ? (
            <p className="text-muted-foreground mt-1 text-sm">{headline(attentionCount, workCount)}</p>
          ) : null}
        </div>
        <LocalDate className="text-muted-foreground text-xs" />
      </header>

      {byTier.attention.length > 0 ? (
        <section aria-labelledby="attention-heading" className="space-y-3">
          <SectionHeading id="attention-heading" icon={AlertTriangle} tone="warning">
            Needs attention
          </SectionHeading>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {byTier.attention.map((queue) => (
              <AttentionCard key={queue.key} queue={queue} />
            ))}
          </div>
        </section>
      ) : null}

      {byTier.work.length > 0 || clear.length > 0 ? (
        <section aria-labelledby="work-heading" className="space-y-3">
          <SectionHeading id="work-heading" icon={ClipboardList}>
            Today&rsquo;s work
          </SectionHeading>
          {byTier.work.length > 0 ? (
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              {byTier.work.map((queue) => (
                <WorkCard key={queue.key} queue={queue} />
              ))}
            </div>
          ) : (
            <Card className="flex items-center gap-3 p-5 text-sm">
              <CheckCircle2 className="h-5 w-5 text-emerald-600" aria-hidden />
              Nothing waiting on you right now.
            </Card>
          )}
          {clear.length > 0 ? (
            <p className="text-muted-foreground text-xs">
              All clear: {clear.map((queue) => queue.label.toLowerCase()).join(" · ")}
            </p>
          ) : null}
        </section>
      ) : null}

      {overview.money || overview.inventory ? (
        <div className="grid gap-6 lg:grid-cols-2">
          {overview.money ? <TradingCard money={overview.money} /> : null}
          {overview.inventory ? <StockCard inventory={overview.inventory} /> : null}
        </div>
      ) : null}

      {byTier.hygiene.length > 0 ? (
        <section aria-labelledby="hygiene-heading" className="space-y-3">
          <SectionHeading id="hygiene-heading" icon={Receipt}>
            Records to tidy
          </SectionHeading>
          <p className="text-muted-foreground -mt-1 text-xs">
            Gaps in the data that quietly make the figures above less accurate.
          </p>
          <Card className="divide-y p-0">
            {byTier.hygiene.map((queue) => (
              <HygieneRow key={queue.key} queue={queue} />
            ))}
          </Card>
        </section>
      ) : null}

      {!hasQueues && !overview.money && !overview.inventory ? (
        <Card className="text-muted-foreground p-8 text-center text-sm">
          Your role does not include any of the areas this page reports on. Ask an owner if you expected to see
          orders, inventory or the books here.
        </Card>
      ) : null}
    </div>
  );
}

/**
 * One line that says what the day looks like. The counts mix orders and items, so it talks about
 * what is waiting rather than calling fourteen listable items fourteen "tasks".
 */
function headline(attention: number, work: number): string {
  if (attention === 0 && work === 0) return "Nothing needs you right now.";
  const parts: string[] = [];
  if (attention > 0) parts.push(`${attention} ${attention === 1 ? "needs" : "need"} attention`);
  if (work > 0) parts.push(`${work} waiting in today’s work`);
  return `${parts.join(" · ")}.`;
}

function SectionHeading({
  id,
  icon: Icon,
  tone = "default",
  children,
}: {
  id: string;
  icon: LucideIcon;
  tone?: "default" | "warning";
  children: React.ReactNode;
}) {
  return (
    <h2 id={id} className="flex items-center gap-2 text-sm font-semibold">
      <Icon className={tone === "warning" ? "h-4 w-4 text-amber-600" : "text-muted-foreground h-4 w-4"} aria-hidden />
      {children}
    </h2>
  );
}

function AttentionCard({ queue }: { queue: PresentedQueue }) {
  return (
    <Link
      href={queue.href}
      className="group rounded-xl border border-amber-300 bg-amber-50 p-5 transition-colors hover:border-amber-400 dark:border-amber-800 dark:bg-amber-950/40"
    >
      <div className="flex items-start justify-between gap-3">
        <p className="text-3xl font-semibold tabular-nums">{queue.count}</p>
        <ArrowRight className="text-muted-foreground h-4 w-4 transition-transform group-hover:translate-x-0.5" aria-hidden />
      </div>
      <p className="mt-2 text-sm font-medium">{queue.label}</p>
      <p className="text-muted-foreground mt-1 text-xs">{queue.why}</p>
    </Link>
  );
}

function WorkCard({ queue }: { queue: PresentedQueue }) {
  const Icon = QUEUE_ICONS[queue.key ?? ""] ?? ClipboardList;
  return (
    <Link href={queue.href} className="group hover:border-brand-300 bg-card flex items-center gap-4 rounded-xl border p-4 transition-colors">
      <div className="bg-brand-50 text-brand-700 dark:bg-brand-900 dark:text-brand-100 flex h-10 w-10 shrink-0 items-center justify-center rounded-lg">
        <Icon className="h-5 w-5" aria-hidden />
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium">{queue.label}</p>
        <p className="text-muted-foreground truncate text-xs">{queue.why}</p>
      </div>
      <p className="text-2xl font-semibold tabular-nums">{queue.count}</p>
    </Link>
  );
}

function HygieneRow({ queue }: { queue: PresentedQueue }) {
  return (
    <Link href={queue.href} className="hover:bg-muted/40 flex items-center gap-4 px-5 py-3 transition-colors">
      <span className="w-12 shrink-0 text-right text-lg font-semibold tabular-nums">{queue.count}</span>
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-medium">{queue.label}</span>
        <span className="text-muted-foreground block text-xs">
          {queue.why}
          {queue.amount ? ` ${money(queue.amount)} not yet on any item.` : ""}
        </span>
      </span>
      <ArrowRight className="text-muted-foreground h-4 w-4 shrink-0" aria-hidden />
    </Link>
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
  const color = tone === "up" ? "text-emerald-600" : tone === "down" ? "text-destructive" : "text-muted-foreground";
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
