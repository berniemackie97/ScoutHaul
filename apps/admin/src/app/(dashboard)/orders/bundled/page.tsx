import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { BundledSaleCreator } from "./bundled-creator";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

export default async function BundledSalePage() {
  const refused = await refusal("/orders/bundled");
  if (refused) return refused;

  const api = await getApiClient();
  const { data: reference } = await api.GET("/api/sales/reference-data");

  return (
    <div className="space-y-6">
      <div>
        <Link href="/orders" className="text-muted-foreground inline-flex items-center gap-1 text-sm hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Orders
        </Link>
        <h1 className="mt-2 text-2xl font-semibold tracking-tight">Record bundled sale</h1>
        <p className="text-muted-foreground mt-1 text-sm">One order, several items, one package/tracking — the total and fees split across items, each keeping its own profit.</p>
      </div>
      <BundledSaleCreator channels={reference?.channels ?? []} />
    </div>
  );
}
