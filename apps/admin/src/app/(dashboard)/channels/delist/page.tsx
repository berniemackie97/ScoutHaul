import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { DelistQueue } from "./delist-queue";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

export default async function DelistPage() {
  const refused = await refusal("/channels/delist");
  if (refused) return refused;

  const api = await getApiClient();
  const { data } = await api.GET("/api/channel-listings/pending-delist", {});
  const listings = data ?? [];

  return (
    <div className="space-y-6">
      <header className="space-y-3">
        <Link href="/channels" className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm">
          <ArrowLeft className="h-4 w-4" /> Channels
        </Link>
        <div>
          <p className="text-muted-foreground text-sm">Cross-posting</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Still for sale after selling</h1>
          <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
            These items sold on one marketplace and are still live on another, where someone can buy them again — and
            a second buyer has to be refunded and apologised to. Open each listing and end it on the marketplace
            itself, then say so here. Nothing is removed for you: taking a listing down with software is against
            every marketplace&rsquo;s terms, so it stays your click.
          </p>
        </div>
      </header>

      <DelistQueue listings={listings} />
    </div>
  );
}
