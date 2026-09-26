import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { SoldQueue } from "./sold-queue";

export const dynamic = "force-dynamic";

export default async function SoldNotRecordedPage() {
  const api = await getApiClient();
  const { data } = await api.GET("/api/platform-listings/sold-unrecorded", {});

  return (
    <div className="space-y-6">
      <header className="space-y-3">
        <Link href="/channels" className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm">
          <ArrowLeft className="h-4 w-4" /> Channels
        </Link>
        <div>
          <p className="text-muted-foreground text-sm">Cross-posting</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Sold on a marketplace, not on the books</h1>
          <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
            A marketplace shows these listings as sold, and no sale here accounts for them — so the money is missing
            and the items still count as stock. Poshmark and Depop do not hand over their sales, so a listing turning
            sold is the whole of the news from them. Recording the sale happens on the item, where the fees go.
          </p>
        </div>
      </header>

      <SoldQueue listings={data ?? []} />
    </div>
  );
}
