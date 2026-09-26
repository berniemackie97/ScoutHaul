import Link from "next/link";
import { ArrowLeft, FileSpreadsheet } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { ListingsBoard, type ListingMatchTarget } from "./listings-board";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

const VIEWS = ["Pending", "Linked", "Ignored"] as const;
type View = (typeof VIEWS)[number];

function safeView(value: string | undefined): View {
  return VIEWS.includes(value as View) ? (value as View) : "Pending";
}

export default async function ListingsPage({
  searchParams,
}: {
  searchParams: Promise<{ view?: string }>;
}) {
  const refused = await refusal("/imports/listings");
  if (refused) return refused;

  const { view: rawView } = await searchParams;
  const activeView = safeView(rawView);

  const api = await getApiClient();
  const [pendingResult, linkedResult, ignoredResult, inventoryResult] = await Promise.all([
    api.GET("/api/platform-listings", { params: { query: { reconcileStatus: "Pending" } } }),
    api.GET("/api/platform-listings", { params: { query: { reconcileStatus: "Linked" } } }),
    api.GET("/api/platform-listings", { params: { query: { reconcileStatus: "Ignored" } } }),
    api.GET("/api/inventory", { params: { query: { includeArchived: false } } }),
  ]);

  const byView = {
    Pending: pendingResult.data ?? [],
    Linked: linkedResult.data ?? [],
    Ignored: ignoredResult.data ?? [],
  } satisfies Record<View, NonNullable<typeof pendingResult.data>>;

  const targets: ListingMatchTarget[] = (inventoryResult.data ?? [])
    .filter((item) => item.id && (!item.isBundle || item.bundleComponentCount === 0))
    .map((item) => ({
      id: item.id as string,
      title: item.title ?? "Untitled item",
      category: item.category,
      isSold: item.isSold ?? false,
    }));

  return (
    <div className="space-y-6">
      <header className="space-y-3">
        <Link
          href="/imports"
          className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm"
        >
          <ArrowLeft className="h-4 w-4" /> Imports
        </Link>
        <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
          <div>
            <p className="text-muted-foreground text-sm">Data</p>
            <h1 className="mt-1 text-2xl font-semibold tracking-tight">Listings to match</h1>
            <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
              Imported or intentionally captured marketplace listing records — active, draft, ended,
              sold — in one place. Link each to the right inventory item, seed a new item, or ignore
              it with visible feedback.
            </p>
          </div>
          <Link
            href="/imports"
            className="hover:bg-muted inline-flex h-10 shrink-0 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium"
          >
            <FileSpreadsheet className="h-4 w-4" /> Import listings
          </Link>
        </div>
      </header>

      <div className="flex flex-wrap gap-2">
        {VIEWS.map((view) => (
          <Link
            key={view}
            href={view === "Pending" ? "/imports/listings" : `/imports/listings?view=${view}`}
            className={`rounded-full border px-3 py-1 text-sm transition ${
              activeView === view
                ? "border-foreground bg-foreground text-background"
                : "text-muted-foreground hover:bg-muted"
            }`}
          >
            {view} ({byView[view].length})
          </Link>
        ))}
      </div>

      <ListingsBoard
        listings={byView[activeView]}
        targets={targets}
        emptyMessage={
          activeView === "Pending"
            ? "Nothing to decide. Import an eBay Active Listings report from the Imports page."
            : activeView === "Linked"
              ? "No linked listings yet."
              : "No ignored listings."
        }
      />
    </div>
  );
}
