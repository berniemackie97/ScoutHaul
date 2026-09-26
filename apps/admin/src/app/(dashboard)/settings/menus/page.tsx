import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { MenusManager } from "./menus-manager";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

export default async function MenusPage() {
  const refused = await refusal("/settings/menus");
  if (refused) return refused;

  const api = await getApiClient();
  const [{ data: referenceData }, { data: overrides }] = await Promise.all([
    api.GET("/api/inventory/reference-data"),
    api.GET("/api/reference-overrides", {}),
  ]);
  if (!referenceData) throw new Error("Reference data could not be loaded.");

  return (
    <div className="space-y-6">
      <div>
        <Link href="/settings" className="text-muted-foreground inline-flex items-center gap-1 text-sm hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Settings
        </Link>
        <h1 className="mt-2 text-2xl font-semibold tracking-tight">Menus &amp; controlled values</h1>
        <p className="text-muted-foreground mt-1 text-sm">Tailor the dropdowns your team picks from across add and edit forms.</p>
      </div>
      <MenusManager referenceData={referenceData} overrides={overrides ?? []} />
    </div>
  );
}
