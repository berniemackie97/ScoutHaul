import Link from "next/link";
import {
  ArrowLeft,
  ArrowUpRight,
  BarChart3,
  Camera,
  FileSpreadsheet,
  MousePointerClick,
  Tags,
} from "lucide-react";
import { Badge, Card } from "@overhaul/ui";
import { getApiAccessToken, getApiBaseUrl } from "@/lib/api";

export const dynamic = "force-dynamic";

type QualitySnapshot = {
  id: string;
  platform: string;
  externalListingId: string;
  title: string;
  categoryName?: string | null;
  listingCondition?: string | null;
  priceRange?: string | null;
  reportGeneratedAt: string;
  importedAt: string;
  platformListingId?: string | null;
  action?: string | null;
  dailyImpressions?: number | null;
  clickThroughRate?: number | null;
  salesConversionRate?: number | null;
  promotedListingsAdRate?: number | null;
  recommendedItemSpecificsFilled?: number | null;
  recommendedItemSpecificsToAdd?: string | null;
  brand?: string | null;
  photoCount?: number | null;
  titleKeywordCount?: number | null;
  freeShipping?: boolean | null;
  estimatedHandlingDays?: number | null;
  acceptsReturns?: boolean | null;
  promotedListings?: boolean | null;
  bestOffer?: boolean | null;
};

type CategoryRollup = {
  category: string;
  count: number;
  impressions: number;
  averageCtr: number | null;
  averageConversion: number | null;
  photoGaps: number;
  specificsGaps: number;
  unpromoted: number;
};

const number = (value: number | null | undefined, digits = 0) =>
  new Intl.NumberFormat("en-US", {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(value ?? 0);

const percent = (value: number | null | undefined) =>
  value == null ? "—" : `${(value * 100).toFixed(2)}%`;

const dateLabel = (value: string | null | undefined) =>
  value
    ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" }).format(
        new Date(value)
      )
    : "—";

export default async function ListingQualityPage() {
  const snapshots = await getQualitySnapshots();
  const latest = latestByListing(snapshots);
  const categories = categoryRollups(latest);
  const actionRows = actionBoard(latest);

  const total = latest.length;
  const matched = latest.filter((row) => row.platformListingId).length;
  const totalImpressions = latest.reduce((sum, row) => sum + (row.dailyImpressions ?? 0), 0);
  const averageCtr = average(latest.map((row) => row.clickThroughRate));
  const photoGaps = latest.filter((row) => (row.photoCount ?? 0) > 0 && (row.photoCount ?? 0) < 8).length;
  const specificsGaps = latest.filter((row) => Boolean(row.recommendedItemSpecificsToAdd)).length;
  const latestReportDate = latest[0]?.reportGeneratedAt;

  return (
    <div className="space-y-6">
      <header className="space-y-3">
        <Link
          href="/analytics"
          className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm"
        >
          <ArrowLeft className="h-4 w-4" /> Reports
        </Link>
        <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
          <div>
            <p className="text-muted-foreground text-sm">Data</p>
            <h1 className="mt-1 flex items-center gap-2 text-2xl font-semibold tracking-tight">
              <BarChart3 className="h-6 w-6" /> eBay listing quality
            </h1>
            <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
              Latest imported Listing Quality workbook metrics. Use this to identify high-impression
              listings with weak CTR, missing photos, item-specific gaps, and promotion gaps.
            </p>
          </div>
          <Link
            href="/imports"
            className="hover:bg-muted inline-flex h-10 shrink-0 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium"
          >
            <FileSpreadsheet className="h-4 w-4" /> Import report
          </Link>
        </div>
      </header>

      {total === 0 ? (
        <Card className="p-8 text-center">
          <h2 className="text-lg font-semibold">No listing quality data yet</h2>
          <p className="text-muted-foreground mx-auto mt-2 max-w-xl text-sm">
            Import the eBay Listing Quality workbook from the Imports page. The workbook should be
            the `.xlsx` file from Seller Hub → Performance → Listing Quality Report.
          </p>
          <Link
            href="/imports"
            className="bg-brand-600 hover:bg-brand-700 mt-5 inline-flex h-10 items-center rounded-lg px-4 text-sm font-medium text-white"
          >
            Go to imports
          </Link>
        </Card>
      ) : (
        <>
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
            <Badge variant="outline">Latest report {dateLabel(latestReportDate)}</Badge>
            <span>{matched} of {total} matched to imported eBay listings</span>
          </div>

          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Kpi label="Listings tracked" value={number(total)} detail={`${matched} matched by eBay item ID`} icon={Tags} />
            <Kpi label="Daily impressions" value={number(totalImpressions)} detail="Across latest listing snapshots" icon={BarChart3} />
            <Kpi label="Average CTR" value={percent(averageCtr)} detail="Clicks divided by impressions" icon={MousePointerClick} />
            <Kpi label="Fix opportunities" value={number(photoGaps + specificsGaps)} detail={`${photoGaps} photo · ${specificsGaps} item-specific`} icon={Camera} />
          </div>

          <Card className="p-6">
            <div className="mb-5">
              <h2 className="font-semibold">Category performance</h2>
              <p className="text-muted-foreground text-sm">
                Rollup from the latest quality snapshot per listing. Bars show relative daily
                impressions.
              </p>
            </div>
            <div className="space-y-3">
              {categories.map((category) => (
                <CategoryRow key={category.category} category={category} max={categories[0]?.impressions ?? 1} />
              ))}
            </div>
          </Card>

          <Card className="overflow-hidden">
            <div className="border-b p-6">
              <h2 className="font-semibold">Action board</h2>
              <p className="text-muted-foreground text-sm">
                Prioritized rows: high impressions, low CTR, missing photos, missing item specifics,
                and unpromoted listings.
              </p>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-muted/40 text-muted-foreground text-left text-xs uppercase tracking-wide">
                  <tr>
                    <th className="px-4 py-3">Listing</th>
                    <th className="px-4 py-3">Category</th>
                    <th className="px-4 py-3 text-right">Impr.</th>
                    <th className="px-4 py-3 text-right">CTR</th>
                    <th className="px-4 py-3 text-right">Conv.</th>
                    <th className="px-4 py-3 text-right">Photos</th>
                    <th className="px-4 py-3">Gaps</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {actionRows.map((row) => (
                    <tr key={`${row.externalListingId}-${row.reportGeneratedAt}`}>
                      <td className="max-w-xl px-4 py-3">
                        <div className="flex items-start gap-2">
                          <Link
                            href={`https://www.ebay.com/itm/${row.externalListingId}`}
                            target="_blank"
                            rel="noreferrer"
                            className="font-medium hover:underline"
                          >
                            {row.title}
                          </Link>
                          <ArrowUpRight className="mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                        </div>
                        <p className="text-muted-foreground mt-1 text-xs">
                          {row.externalListingId} · {row.listingCondition ?? "Unknown condition"}
                        </p>
                      </td>
                      <td className="px-4 py-3 text-muted-foreground">{row.categoryName ?? "Uncategorized"}</td>
                      <td className="px-4 py-3 text-right tabular-nums">{number(row.dailyImpressions)}</td>
                      <td className="px-4 py-3 text-right tabular-nums">{percent(row.clickThroughRate)}</td>
                      <td className="px-4 py-3 text-right tabular-nums">{percent(row.salesConversionRate)}</td>
                      <td className="px-4 py-3 text-right tabular-nums">{row.photoCount ?? "—"}</td>
                      <td className="px-4 py-3">
                        <div className="flex flex-wrap gap-1.5">
                          {(row.photoCount ?? 0) > 0 && (row.photoCount ?? 0) < 8 && <Badge variant="outline">photos</Badge>}
                          {row.recommendedItemSpecificsToAdd && <Badge variant="outline">specifics</Badge>}
                          {row.promotedListings === false && <Badge variant="outline">not promoted</Badge>}
                          {row.bestOffer === false && <Badge variant="outline">no offer</Badge>}
                          {!row.recommendedItemSpecificsToAdd && !((row.photoCount ?? 0) > 0 && (row.photoCount ?? 0) < 8) && row.promotedListings !== false && row.bestOffer !== false && (
                            <span className="text-muted-foreground">—</span>
                          )}
                        </div>
                      </td>
                    </tr>
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

function Kpi({
  label,
  value,
  detail,
  icon: Icon,
}: {
  label: string;
  value: string;
  detail: string;
  icon: typeof BarChart3;
}) {
  return (
    <Card className="p-5">
      <div className="flex items-center justify-between">
        <span className="text-muted-foreground text-sm">{label}</span>
        <Icon className="text-muted-foreground h-4 w-4" />
      </div>
      <p className="mt-3 text-3xl font-semibold tracking-tight tabular-nums">{value}</p>
      <p className="text-muted-foreground mt-1 text-xs">{detail}</p>
    </Card>
  );
}

function CategoryRow({ category, max }: { category: CategoryRollup; max: number }) {
  const width = Math.max(4, Math.round((category.impressions / Math.max(1, max)) * 100));
  return (
    <div className="rounded-lg border p-4">
      <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-start">
        <div>
          <h3 className="font-medium">{category.category}</h3>
          <p className="text-muted-foreground text-xs">
            {category.count} listings · {number(category.impressions)} daily impressions
          </p>
        </div>
        <div className="text-muted-foreground flex flex-wrap gap-3 text-xs">
          <span>CTR {percent(category.averageCtr)}</span>
          <span>Conv. {percent(category.averageConversion)}</span>
          <span>{category.photoGaps} photo gaps</span>
          <span>{category.specificsGaps} specifics gaps</span>
        </div>
      </div>
      <div className="bg-muted mt-3 h-2 overflow-hidden rounded-full">
        <div className="bg-brand-600 h-full rounded-full" style={{ width: `${width}%` }} />
      </div>
    </div>
  );
}

async function getQualitySnapshots(): Promise<QualitySnapshot[]> {
  const token = await getApiAccessToken();
  const response = await fetch(`${getApiBaseUrl()}/api/platform-listings/quality-snapshots?take=500`, {
    cache: "no-store",
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  });
  if (!response.ok) return [];
  return (await response.json()) as QualitySnapshot[];
}

function latestByListing(snapshots: QualitySnapshot[]): QualitySnapshot[] {
  const sorted = snapshots
    .slice()
    .sort(
      (left, right) =>
        new Date(right.reportGeneratedAt).getTime() - new Date(left.reportGeneratedAt).getTime() ||
        new Date(right.importedAt).getTime() - new Date(left.importedAt).getTime()
    );
  const byListing = new Map<string, QualitySnapshot>();
  for (const snapshot of sorted) {
    if (!byListing.has(snapshot.externalListingId)) byListing.set(snapshot.externalListingId, snapshot);
  }
  return [...byListing.values()];
}

function categoryRollups(rows: QualitySnapshot[]): CategoryRollup[] {
  const groups = new Map<string, QualitySnapshot[]>();
  for (const row of rows) {
    const key = row.categoryName || "Uncategorized";
    groups.set(key, [...(groups.get(key) ?? []), row]);
  }

  return [...groups.entries()]
    .map(([category, items]) => ({
      category,
      count: items.length,
      impressions: items.reduce((sum, row) => sum + (row.dailyImpressions ?? 0), 0),
      averageCtr: average(items.map((row) => row.clickThroughRate)),
      averageConversion: average(items.map((row) => row.salesConversionRate)),
      photoGaps: items.filter((row) => (row.photoCount ?? 0) > 0 && (row.photoCount ?? 0) < 8).length,
      specificsGaps: items.filter((row) => Boolean(row.recommendedItemSpecificsToAdd)).length,
      unpromoted: items.filter((row) => row.promotedListings === false).length,
    }))
    .sort((left, right) => right.impressions - left.impressions);
}

function actionBoard(rows: QualitySnapshot[]): QualitySnapshot[] {
  return rows
    .slice()
    .sort((left, right) => score(right) - score(left))
    .slice(0, 100);
}

function score(row: QualitySnapshot): number {
  const impressions = row.dailyImpressions ?? 0;
  const lowCtrPenalty = row.clickThroughRate == null || row.clickThroughRate < 0.005 ? 1 : 0.4;
  const photoGap = (row.photoCount ?? 0) > 0 && (row.photoCount ?? 0) < 8 ? 30 : 0;
  const specificsGap = row.recommendedItemSpecificsToAdd ? 40 : 0;
  const promotionGap = row.promotedListings === false ? 20 : 0;
  return impressions * lowCtrPenalty + photoGap + specificsGap + promotionGap;
}

function average(values: Array<number | null | undefined>): number | null {
  const usable = values.filter((value): value is number => typeof value === "number");
  if (usable.length === 0) return null;
  return usable.reduce((sum, value) => sum + value, 0) / usable.length;
}
