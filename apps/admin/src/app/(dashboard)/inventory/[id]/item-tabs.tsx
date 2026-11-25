import Link from "next/link";

export const ITEM_TABS = ["details", "photos", "channels"] as const;
export type ItemTab = (typeof ITEM_TABS)[number];

export function readItemTab(value: string | undefined): ItemTab {
  return ITEM_TABS.includes(value as ItemTab) ? (value as ItemTab) : "details";
}

const LABELS: Record<ItemTab, string> = { details: "Details", photos: "Photos", channels: "Channels" };

/** Links, not client state: each tab is a URL, so it survives a reload and a server action's refresh. */
export function ItemTabs({ id, active, counts }: { id: string; active: ItemTab; counts: Partial<Record<ItemTab, number>> }) {
  return (
    <nav aria-label="Item sections" className="flex flex-wrap gap-1 border-b">
      {ITEM_TABS.map((tab) => {
        const current = tab === active;
        const count = counts[tab];
        return (
          <Link
            key={tab}
            href={tab === "details" ? `/inventory/${id}` : `/inventory/${id}?tab=${tab}`}
            aria-current={current ? "page" : undefined}
            className={`-mb-px inline-flex items-center gap-2 border-b-2 px-3.5 py-2.5 text-sm ${current ? "border-primary font-semibold" : "text-muted-foreground hover:text-foreground border-transparent"}`}
          >
            {LABELS[tab]}
            {count !== undefined && count > 0 && <span className="bg-muted rounded-full px-2 text-xs tabular-nums">{count}</span>}
          </Link>
        );
      })}
    </nav>
  );
}
