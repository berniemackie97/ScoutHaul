import Link from "next/link";
import { ArrowRight, FileSpreadsheet } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { CsvUploader } from "./csv-uploader";
import {
  importEbayOrders,
  importEbayListings,
  importEbayListingQuality,
  importMarketplaceRefunds,
} from "./actions";

export const dynamic = "force-dynamic";

export default async function ImportsPage() {
  const api = await getApiClient();
  const [orders, listings] = await Promise.all([
    api.GET("/api/platform-orders", { params: { query: { status: "Imported" } } }),
    api.GET("/api/platform-listings", { params: { query: { reconcileStatus: "Pending" } } }),
  ]);
  const ordersToReconcile = orders.data?.length ?? 0;
  const listingsToReconcile = listings.data?.length ?? 0;

  return (
    <div className="space-y-6">
      <header>
        <p className="text-muted-foreground text-sm">Data</p>
        <h1 className="mt-1 flex items-center gap-2 text-2xl font-semibold tracking-tight">
          <FileSpreadsheet className="h-6 w-6" /> Imports
        </h1>
        <p className="text-muted-foreground mt-1 max-w-2xl text-sm">
          Bring a marketplace&apos;s own exports into ScoutHaul. Drop a file — anything that matches exactly is recorded for you, and only genuine questions reach
          results into orders, customers, sales, and listings. Re-importing the same file updates in
          place — it never duplicates.
        </p>
      </header>

      <div className="grid gap-4 lg:grid-cols-4">
        <CsvUploader
          title="eBay orders"
          description="Seller Hub → Orders (All Orders report) for buyers + tracking, or Performance → Sales for exact fees. The importer detects which and merges them."
          action={importEbayOrders}
          buttonLabel="Import eBay orders"
        />
        <CsvUploader
          title="eBay listings"
          description="Seller Hub → Listings → Active → Download. Seeds or links inventory from listings that predate ScoutHaul."
          action={importEbayListings}
          buttonLabel="Import eBay listings"
        />
        <CsvUploader
          title="eBay listing quality"
          description="Seller Hub → Performance → Listing Quality Report (.xlsx). Imports impressions, CTR, conversion, photo counts, and recommendation gaps by listing."
          action={importEbayListingQuality}
          accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
          fileLabel="XLSX workbook"
          buttonLabel="Import quality"
          allowPaste={false}
        />
        <CsvUploader
          title="Marketplace refunds"
          description="Depop Sales refund columns or eBay Listing Sales fee-credit refund rows. Only exact sale matches are posted."
          action={importMarketplaceRefunds}
          buttonLabel="Import refunds"
        />
      </div>

      <section className="grid gap-4 sm:grid-cols-3">
        <ReconcileCard
          href="/imports/orders"
          title="Orders to match"
          count={ordersToReconcile}
          hint="Match imported orders to inventory → creates sales with exact fees."
        />
        <ReconcileCard
          href="/imports/listings"
          title="Listings to match"
          count={listingsToReconcile}
          hint="Link imported listings to an inventory item, or seed a new draft."
        />
      </section>
    </div>
  );
}

function ReconcileCard({
  href,
  title,
  count,
  hint,
}: {
  href: string;
  title: string;
  count: number;
  hint: string;
}) {
  return (
    <Link
      href={href}
      className="bg-card hover:border-foreground/20 group rounded-xl border p-5 transition-colors"
    >
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium">{title}</span>
        <ArrowRight className="text-muted-foreground group-hover:text-foreground h-4 w-4" />
      </div>
      <p className="mt-2 text-3xl font-semibold tabular-nums">{count}</p>
      <p className="text-muted-foreground mt-1 text-xs">{hint}</p>
    </Link>
  );
}
