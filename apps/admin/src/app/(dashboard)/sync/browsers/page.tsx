import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { Card } from "@overhaul/ui";
import { PageHeader } from "@/components/list/list-search";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";
import { BrowserList } from "./browser-list";
import { ConnectBrowser } from "./connect-browser";

export const dynamic = "force-dynamic";

/** The browsers paired to sync, what each can see, and connecting another. */
export default async function BrowsersPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const query = await searchParams;
  const api = await getApiClient();
  const { data: overview, error } = await api.GET("/api/sync/overview", {});

  return (
    <div className="space-y-8">
      <Link href="/sync" className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm">
        <ArrowLeft className="h-4 w-4" aria-hidden /> Sync
      </Link>
      <PageHeader
        eyebrow="Sync"
        title="Browsers"
        description="Each browser paired here can sync the marketplace accounts it is signed in to — and nothing else. Its token works only for syncing, rotates on its own, and stops the moment you disconnect it."
      />

      <ConnectBrowser startOpen={query.connect === "1"} />

      {!overview ? (
        <Card className="p-6 text-sm">{errorMessage(error, "The browsers could not be loaded.")}</Card>
      ) : (
        <BrowserList agents={overview.agents ?? []} />
      )}
    </div>
  );
}
