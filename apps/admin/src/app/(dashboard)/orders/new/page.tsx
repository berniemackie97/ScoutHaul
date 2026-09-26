import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { SaleCreator } from "./sale-creator";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

export default async function NewSalePage() {
  const refused = await refusal("/orders/new");
  if (refused) return refused;

  const api = await getApiClient();
  const [{ data: candidates }, { data: reference }] = await Promise.all([
    api.GET("/api/sales/candidates"),
    api.GET("/api/sales/reference-data"),
  ]);

  return (
    <div className="space-y-6">
      <header>
        <Link href="/orders" className="text-muted-foreground hover:text-foreground inline-flex items-center gap-2 text-sm">
          <ArrowLeft className="h-4 w-4" /> Orders
        </Link>
        <h1 className="mt-4 text-2xl font-semibold tracking-tight">Record a sale</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          Create a current or historical sale from canonical inventory, channel, tax, fee, and cost data.
        </p>
      </header>
      <SaleCreator candidates={candidates ?? []} channels={reference?.channels ?? []} />
    </div>
  );
}
