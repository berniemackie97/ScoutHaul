import Link from "next/link";
import type { components } from "@overhaul/api-client";
import { AlertTriangle, ArrowRight, CalendarClock, Info, Landmark, OctagonAlert, Settings2 } from "lucide-react";
import { Badge, Card, buttonVariants } from "@overhaul/ui";
import { PageHeader } from "@/components/list/list-search";
import { getApiClient } from "@/lib/api";
import { refusal } from "@/lib/page-access";
import { cents, countdown, day, frequencyLabel, percent, severityTone, statusLabel, statusVariant } from "@/features/tax/tax-format";

export const dynamic = "force-dynamic";

type Period = components["schemas"]["SalesTaxPeriodSummaryDto"];
type Notice = components["schemas"]["SalesTaxNoticeDto"];

export default async function SalesTaxPage() {
  const refused = await refusal("/tax");
  if (refused) return refused;

  const api = await getApiClient();
  const { data: overview } = await api.GET("/api/sales-tax/overview");

  if (!overview) {
    return (
      <div className="space-y-4">
        <PageHeader eyebrow="Compliance" title="Sales tax" />
        <div className="border-destructive/30 bg-destructive/5 text-destructive rounded-xl border p-6 text-sm">
          The tax overview could not be loaded. Try again in a moment.
        </div>
      </div>
    );
  }

  const profile = overview.profile;
  const configured = profile?.configured ?? false;
  const frequency = profile?.frequencies?.at(-1);

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Compliance"
        title="Sales tax — Florida"
        description="Every DR-15 return owed since registration, what goes on each line, and what was filed. Marketplace sales are left off the return because the marketplace already remits the tax."
        action={
          <Link href="/tax/settings" className={buttonVariants({ variant: "outline", size: "sm" })}>
            <Settings2 className="h-4 w-4" aria-hidden /> Registration
          </Link>
        }
      />

      {!configured ? (
        <Card className="p-6">
          <div className="flex items-start gap-4">
            <Landmark className="text-muted-foreground mt-0.5 h-6 w-6 shrink-0" aria-hidden />
            <div className="space-y-2">
              <h2 className="text-lg font-semibold">Set up the Florida registration</h2>
              <p className="text-muted-foreground max-w-2xl text-sm">
                Enter the certificate number, county, the date the registration took effect and the filing frequency DOR assigned.
                From those, every reporting period is generated, along with its due date and whether it was filed.
              </p>
              <Link href="/tax/settings" className={buttonVariants({ size: "sm" })}>
                Add registration <ArrowRight className="h-4 w-4" aria-hidden />
              </Link>
            </div>
          </div>
        </Card>
      ) : (
        <>
          <div className="grid gap-4 lg:grid-cols-3">
            <NextDueCard period={overview.nextDue ?? null} />
            <Card className="p-5">
              <p className="text-muted-foreground text-sm">Registration</p>
              <dl className="mt-3 space-y-2 text-sm">
                <Row label="Certificate" value={profile?.certificateNumber ?? "Not entered"} />
                <Row label="County" value={`${profile?.county} · ${percent(profile?.countySurtaxRate)} surtax · ${percent(0.06 + (profile?.countySurtaxRate ?? 0))} total`} />
                <Row label="Frequency" value={frequency?.frequency ? frequencyLabel[frequency.frequency] : "—"} />
                <Row label="Files" value={profile?.filesElectronically ? "Electronically (eServices)" : "On paper"} />
                <Row label="Registered" value={day(profile?.registeredOn)} />
              </dl>
            </Card>
            <Card className="p-5">
              <p className="text-muted-foreground text-sm">Last 12 months</p>
              <p className="mt-3 text-3xl font-semibold tabular-nums">{cents(overview.taxRemittedLastFourQuarters)}</p>
              <p className="text-muted-foreground mt-1 text-xs">Florida tax owed on returns ending in the last year.</p>
              <p className="mt-3 text-sm">
                {overview.overdueCount ? (
                  <span className="text-destructive font-medium">{overview.overdueCount} return{overview.overdueCount === 1 ? "" : "s"} overdue</span>
                ) : (
                  <span className="text-green-700 dark:text-green-400">Nothing overdue</span>
                )}
              </p>
            </Card>
          </div>

          {overview.notices && overview.notices.length > 0 ? <Notices notices={overview.notices} /> : null}

          <Card className="overflow-hidden">
            <div className="border-b px-5 py-4">
              <h2 className="font-semibold">Reporting periods</h2>
              <p className="text-muted-foreground text-sm">Newest first. Open any period for its worksheet.</p>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[860px] text-sm">
                <thead className="bg-muted/40 text-muted-foreground text-left text-xs uppercase tracking-wide">
                  <tr>
                    <th className="px-5 py-3 font-medium">Period</th>
                    <th className="px-3 py-3 font-medium">Status</th>
                    <th className="px-3 py-3 font-medium">Late after</th>
                    <th className="px-3 py-3 text-right font-medium">Reported sales</th>
                    <th className="px-3 py-3 text-right font-medium">Marketplace (excluded)</th>
                    <th className="px-3 py-3 text-right font-medium">Tax due</th>
                    <th className="px-3 py-3 text-right font-medium">With return</th>
                    <th className="px-5 py-3 font-medium">Filed</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {(overview.periods ?? []).map((period) => (
                    <PeriodRow key={period.key} period={period} />
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
        </>
      )}
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right font-medium">{value}</dd>
    </div>
  );
}

function NextDueCard({ period }: { period: Period | null }) {
  if (!period) {
    return (
      <Card className="p-5">
        <p className="text-muted-foreground text-sm">Next return</p>
        <p className="mt-3 text-lg font-semibold">Nothing due</p>
      </Card>
    );
  }

  const overdue = period.status === "Overdue";
  return (
    <Card className={`p-5 ${overdue ? "border-destructive/50" : period.status === "DueSoon" ? "border-amber-400" : ""}`}>
      <div className="flex items-center justify-between">
        <p className="text-muted-foreground text-sm">{overdue ? "Overdue return" : period.status === "Open" ? "Current period" : "Next return due"}</p>
        <CalendarClock className="text-muted-foreground h-4 w-4" aria-hidden />
      </div>
      <p className="mt-3 text-lg font-semibold">{period.label}</p>
      <p className={`mt-1 text-sm font-medium ${overdue ? "text-destructive" : period.status === "DueSoon" ? "text-amber-700 dark:text-amber-400" : ""}`}>
        {period.status === "Open" ? `Period ends ${day(period.end)}` : `Late after ${day(period.lateAfter, "long")}`}
        {period.status !== "Open" ? ` · ${countdown(period.daysUntilLateAfter, period.status)}` : ""}
      </p>
      {period.status !== "Open" && !overdue ? (
        <p className="text-muted-foreground mt-1 text-xs">E-payment must be started by 5:00 p.m. ET on {day(period.electronicPaymentDeadline, "long")}.</p>
      ) : null}
      <p className="mt-3 text-2xl font-semibold tabular-nums">{cents(period.amountDueWithReturn)}</p>
      <p className="text-muted-foreground text-xs">{overdue ? "If filed today, including the late penalty" : "Amount due with the return"}</p>
      <Link href={`/tax/${period.key}`} className="text-brand-700 dark:text-brand-300 mt-4 inline-flex items-center gap-1 text-sm font-medium hover:underline">
        Open worksheet <ArrowRight className="h-4 w-4" aria-hidden />
      </Link>
    </Card>
  );
}

function Notices({ notices }: { notices: Notice[] }) {
  return (
    <div className="space-y-2">
      {notices.map((notice, index) => {
        const severity = notice.severity ?? "Info";
        const Icon = severity === "Blocking" ? OctagonAlert : severity === "Warning" ? AlertTriangle : Info;
        return (
          <div key={`${notice.code}-${index}`} className={`flex items-start gap-3 rounded-lg border px-4 py-3 text-sm ${severityTone[severity]}`}>
            <Icon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
            <p className="flex-1">{notice.message}</p>
            {notice.periodKey ? (
              <Link href={`/tax/${notice.periodKey}`} className="shrink-0 font-medium underline-offset-2 hover:underline">
                Open
              </Link>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}

function PeriodRow({ period }: { period: Period }) {
  const status = period.status ?? "Open";
  const filed = period.filed;
  return (
    <tr className="hover:bg-muted/30">
      <td className="px-5 py-3">
        <Link href={`/tax/${period.key}`} className="font-medium hover:underline">
          {period.label}
        </Link>
        <p className="text-muted-foreground text-xs">
          {day(period.start)} – {day(period.end)}
        </p>
      </td>
      <td className="px-3 py-3">
        <Badge variant={statusVariant[status]}>{statusLabel[status]}</Badge>
        {(period.blockingIssues ?? 0) > 0 ? <p className="text-destructive mt-1 text-xs">{period.blockingIssues} to resolve</p> : null}
      </td>
      <td className="px-3 py-3">
        {day(period.lateAfter)}
        <p className="text-muted-foreground text-xs">{countdown(period.daysUntilLateAfter, status)}</p>
      </td>
      <td className="px-3 py-3 text-right tabular-nums">
        {cents(period.reportedGrossSales)}
        <p className="text-muted-foreground text-xs">{period.reportedSalesCount} sale{period.reportedSalesCount === 1 ? "" : "s"}</p>
      </td>
      <td className="text-muted-foreground px-3 py-3 text-right tabular-nums">
        {cents(period.excludedGrossSales)}
        <p className="text-xs">{period.excludedSalesCount} sale{period.excludedSalesCount === 1 ? "" : "s"}</p>
      </td>
      <td className="px-3 py-3 text-right font-medium tabular-nums">{cents(period.totalTaxDue)}</td>
      <td className="px-3 py-3 text-right tabular-nums">{cents(period.amountDueWithReturn)}</td>
      <td className="px-5 py-3">
        {filed ? (
          <>
            {day(filed.filedOn)}
            <p className="text-muted-foreground text-xs">
              {filed.method}
              {filed.confirmationNumber ? ` · #${filed.confirmationNumber}` : ""}
              {filed.kind === "AmendedReplacement" ? " · amended" : ""}
            </p>
          </>
        ) : (
          <span className="text-muted-foreground">—</span>
        )}
      </td>
    </tr>
  );
}
