import Link from "next/link";
import { ArrowRight, History, MonitorSmartphone } from "lucide-react";
import { Button, Card } from "@overhaul/ui";
import { PageHeader } from "@/components/list/list-search";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";
import { SyncHub } from "./sync-hub";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

/**
 * Sync: which marketplace accounts the paired browsers keep in step, how often, and what they are
 * doing now. Nothing here runs on its own until a schedule is switched on.
 */
export default async function SyncPage() {
  const refused = await refusal("/sync");
  if (refused) return refused;

  const api = await getApiClient();
  const { data: overview, error } = await api.GET("/api/sync/overview", {});

  return (
    <div className="space-y-8">
      <PageHeader
        eyebrow="Data"
        title="Sync"
        description="A browser you sell in, with HaulScout Sync installed, reads your marketplace accounts and brings their sales and listings in — on demand, or on a schedule you switch on. It reads only; it never changes anything on a marketplace."
        action={
          <div className="flex gap-2">
            <Button asChild variant="outline" size="sm">
              <Link href="/sync/browsers">
                <MonitorSmartphone className="mr-2 h-4 w-4" aria-hidden /> Browsers
              </Link>
            </Button>
            <Button asChild variant="outline" size="sm">
              <Link href="/imports">
                <History className="mr-2 h-4 w-4" aria-hidden /> Imports &amp; history
              </Link>
            </Button>
          </div>
        }
      />

      {!overview ? (
        <Card className="p-6 text-sm">{errorMessage(error, "Sync could not be loaded.")}</Card>
      ) : (
        <>
          {(overview.agents ?? []).filter((agent) => !agent.revoked).length === 0 ? <GetStarted /> : null}
          <SyncHub overview={overview} />
        </>
      )}
    </div>
  );
}

function GetStarted() {
  return (
    <Card className="border-brand-200 bg-brand-50/40 p-6">
      <h2 className="text-base font-semibold">Connect the browser you sell in</h2>
      <ol className="text-muted-foreground mt-3 list-decimal space-y-1 pl-5 text-sm">
        <li>Install the HaulScout Sync add-on in Firefox, in the profile where you are signed in to your marketplaces.</li>
        <li>Choose Connect a browser, and paste the code it shows into the add-on.</li>
        <li>Allow the add-on to reach each marketplace when it asks, then bind each account below to the seller account it sees.</li>
      </ol>
      <Button asChild className="mt-4" size="sm">
        <Link href="/sync/browsers?connect=1">
          Connect a browser <ArrowRight className="ml-2 h-4 w-4" aria-hidden />
        </Link>
      </Button>
    </Card>
  );
}
