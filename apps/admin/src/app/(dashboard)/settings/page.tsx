import Link from "next/link";
import { Card, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import { LockKeyhole, Settings2 } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { permission, type Permission } from "@/lib/permissions";
import { getSession } from "@/lib/session";
import { StoreConnectionForm } from "./store-connection-form";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";


export default async function SettingsPage() {
  const refused = await refusal("/settings");
  if (refused) return refused;

  // Only what this role can use is shown; the rest would be forms its requests are refused from.
  const session = await getSession();
  const can = (key: Permission) => !session || (session.permissions ?? []).includes(key);
  const canStore = can(permission.organization);
  const api = await getApiClient();
  const store = canStore ? (await api.GET("/api/store/settings", {})).data : undefined;

  return (
    <div className="space-y-6">
      <header>
        <p className="text-sm text-muted-foreground">System</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Settings</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">Your business details, who can sign in, and the menus your team picks from.</p>
      </header>

      {canStore ? (
        <StoreConnectionForm
          initial={{
            name: store?.name ?? "",
            storeDomain: store?.storeDomain ?? null,
            isLive: store?.isLive ?? false,
            timeZoneId: store?.timeZoneId ?? "America/Los_Angeles",
          }}
        />
      ) : null}

      {can(permission.members) ? (
        <Link href="/settings/team" className="block">
          <Card className="transition hover:border-brand-300 hover:bg-muted/30">
            <CardHeader>
              <div className="bg-brand-100 mb-2 flex h-9 w-9 items-center justify-center rounded-lg"><LockKeyhole className="text-brand-700 h-4 w-4" /></div>
              <CardTitle className="text-lg">Team &amp; access</CardTitle>
              <CardDescription>Invite staff, assign roles and permissions, and manage invite codes.</CardDescription>
            </CardHeader>
          </Card>
        </Link>
      ) : null}

      {can(permission.inventory) ? (
        <Link href="/settings/menus" className="block">
          <Card className="transition hover:border-brand-300 hover:bg-muted/30">
            <CardHeader>
              <div className="bg-brand-100 mb-2 flex h-9 w-9 items-center justify-center rounded-lg"><Settings2 className="text-brand-700 h-4 w-4" /></div>
              <CardTitle className="text-lg">Menus &amp; controlled values</CardTitle>
              <CardDescription>Add your own storage locations, categories, colors, and more — or hide options you don&rsquo;t use.</CardDescription>
            </CardHeader>
          </Card>
        </Link>
      ) : null}
    </div>
  );
}
