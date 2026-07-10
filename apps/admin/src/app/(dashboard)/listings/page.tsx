import Link from "next/link";
import { FileSpreadsheet } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { ListingsBoard } from "./listings-board";

export const dynamic = "force-dynamic";

export default async function ListingsPage() {
  const api = await getApiClient();
  const [pendingResult, linkedResult] = await Promise.all([
    api.GET("/api/platform-listings", { params: { query: { reconcileStatus: "Pending" } } }),
    api.GET("/api/platform-listings", { params: { query: { reconcileStatus: "Linked" } } }),
  ]);
  const pending = pendingResult.data ?? [];
  const linked = linkedResult.data ?? [];

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
        <div>
          <p className="text-muted-foreground text-sm">Marketplace data</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Listing reconciliation</h1>
          <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
            Imported or intentionally captured marketplace listing records — active, draft, ended,
            sold — in one place. Reconcile each into inventory by linking an existing item or
            seeding a new one.
          </p>
        </div>
        <Link
          href="/imports"
          className="hover:bg-muted inline-flex h-10 shrink-0 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium"
        >
          <FileSpreadsheet className="h-4 w-4" /> Import listings
        </Link>
      </header>

      <section className="space-y-3">
        <h2 className="text-sm font-semibold">To reconcile ({pending.length})</h2>
        <ListingsBoard listings={pending} />
      </section>

      {linked.length > 0 && (
        <section className="space-y-3">
          <h2 className="text-sm font-semibold">Linked to inventory ({linked.length})</h2>
          <ListingsBoard listings={linked} />
        </section>
      )}
    </div>
  );
}
