import { getApiClient } from "@/lib/api";
import { ListingsBoard } from "./listings-board";
import { ListingImporter } from "./importer";

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
      <header>
        <p className="text-muted-foreground text-sm">Marketplace data</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Listing imports</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          Imported or intentionally captured marketplace listing records — active, draft, ended, sold — in one place.
          Reconcile each into inventory by linking an existing item or seeding a new one.
        </p>
      </header>

      <section className="space-y-3">
        <h2 className="text-sm font-semibold">To reconcile ({pending.length})</h2>
        <ListingsBoard listings={pending} />
      </section>

      <ListingImporter />

      {linked.length > 0 && (
        <section className="space-y-3">
          <h2 className="text-sm font-semibold">Linked to inventory ({linked.length})</h2>
          <ListingsBoard listings={linked} />
        </section>
      )}
    </div>
  );
}
