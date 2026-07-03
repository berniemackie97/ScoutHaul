import Link from "next/link";
import { Card } from "@overhaul/ui";
import { Radio, Store, Globe, AlertTriangle, ExternalLink } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { ChannelManager } from "./channel-manager";

export const dynamic = "force-dynamic";

export default async function ChannelsPage() {
  const api = await getApiClient();
  const [{ data: accounts }, { data: reference }, { data: delist }] = await Promise.all([
    api.GET("/api/channel-accounts", {}),
    api.GET("/api/sales/reference-data", {}),
    api.GET("/api/channel-listings", { params: { query: { status: "PendingDelist" } } }),
  ]);

  const list = accounts ?? [];
  const pendingDelist = delist ?? [];
  const nativeCount = list.filter((account) => account.isNativelySynced && account.isActive).length;
  const externalCount = list.filter((account) => !account.isNativelySynced && account.isActive).length;

  const kpis = [
    { label: "Active channels", value: String(list.filter((a) => a.isActive).length), detail: "Connected and in use", icon: Radio },
    { label: "Native", value: String(nativeCount), detail: "Storefront — synced by the app", icon: Store },
    { label: "External", value: String(externalCount), detail: "Marketplaces — extension-bridged", icon: Globe },
  ];

  return (
    <div className="space-y-6">
      <header>
        <p className="text-muted-foreground text-sm">Commerce</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Channels</h1>
        <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
          The accounts your inventory lists on. Your storefront syncs natively; external marketplaces (eBay, Poshmark, Depop, Vinted…) are
          bridged by the human-led cross-listing extension. Connect an account here so items can be cross-listed and kept in sync.
        </p>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
        {kpis.map((kpi) => (
          <Card key={kpi.label} className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-muted-foreground text-sm">{kpi.label}</span>
              <kpi.icon className="text-muted-foreground h-4 w-4" aria-hidden />
            </div>
            <p className="mt-3 text-3xl font-semibold tabular-nums">{kpi.value}</p>
            <p className="text-muted-foreground mt-1 text-xs">{kpi.detail}</p>
          </Card>
        ))}
      </div>

      {pendingDelist.length > 0 && (
        <Card className="border-amber-300 p-5">
          <div className="flex items-center gap-2 text-sm font-semibold text-amber-700">
            <AlertTriangle className="h-4 w-4" /> Needs delist ({pendingDelist.length})
          </div>
          <p className="text-muted-foreground mt-1 text-xs">Sold on another channel — take these down where they&rsquo;re still live.</p>
          <div className="mt-3 divide-y">
            {pendingDelist.map((listing) => (
              <div key={listing.id} className="flex items-center justify-between gap-3 py-2 text-sm">
                <Link href={`/inventory/${listing.inventoryItemId}`} className="font-medium hover:underline">
                  {listing.inventoryItemTitle}
                </Link>
                <div className="flex items-center gap-2">
                  <span className="text-muted-foreground text-xs">{listing.channelAccountName}</span>
                  {listing.externalUrl && (
                    <a href={listing.externalUrl} target="_blank" rel="noreferrer" className="text-brand-700" title="Open listing">
                      <ExternalLink className="h-4 w-4" />
                    </a>
                  )}
                </div>
              </div>
            ))}
          </div>
        </Card>
      )}

      <ChannelManager accounts={list} channels={reference?.channels ?? []} />
    </div>
  );
}
