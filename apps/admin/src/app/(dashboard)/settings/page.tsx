import Link from "next/link";
import { Badge, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import { Bell, CreditCard, LockKeyhole, ReceiptText, Settings2, Truck } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { permission, type Permission } from "@/lib/permissions";
import { getSession } from "@/lib/session";
import { StoreConnectionForm } from "./store-connection-form";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

const plannedModules = [
  { title: "Payments and checkout", description: "Payment processors, accepted methods, capture rules, refunds, fraud controls, and webhook health.", icon: CreditCard },
  { title: "Shipping and fulfillment", description: "Warehouses, carriers, service levels, packaging, label defaults, local pickup, and return routing.", icon: Truck },
  { title: "Tax and accounting", description: "Tax registrations, calculation provider, reporting basis, fiscal periods, account mappings, and exports.", icon: ReceiptText },
  { title: "Notifications", description: "Customer and operator templates, delivery channels, event routing, escalation, and quiet hours.", icon: Bell },
];

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
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Settings and governance</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">The control plane for store behavior, financial policy, integrations, security, and staff access.</p>
      </header>

      {canStore ? (
        <StoreConnectionForm
          initial={{
            name: store?.name ?? "",
            storeDomain: store?.storeDomain ?? null,
            isLive: store?.isLive ?? false,
          }}
        />
      ) : null}

      {can(permission.members) ? (
        <Link href="/settings/team" className="block">
          <Card className="transition hover:border-brand-300 hover:bg-muted/30">
            <CardHeader>
              <div className="mb-2 flex items-center justify-between">
                <div className="bg-brand-100 flex h-9 w-9 items-center justify-center rounded-lg"><LockKeyhole className="text-brand-700 h-4 w-4" /></div>
                <Badge variant="success">Live</Badge>
              </div>
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
              <div className="mb-2 flex items-center justify-between">
                <div className="bg-brand-100 flex h-9 w-9 items-center justify-center rounded-lg"><Settings2 className="text-brand-700 h-4 w-4" /></div>
                <Badge variant="success">Live</Badge>
              </div>
              <CardTitle className="text-lg">Menus &amp; controlled values</CardTitle>
              <CardDescription>Add your own storage locations, categories, colors, and more — or hide options you don&rsquo;t use.</CardDescription>
            </CardHeader>
          </Card>
        </Link>
      ) : null}

      <div>
        <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-muted-foreground">More modules</h2>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {plannedModules.map((module) => (
            <Card key={module.title}>
              <CardHeader>
                <div className="mb-3 flex items-center justify-between">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted"><module.icon className="h-4 w-4" /></div>
                  <Badge variant="outline">Planned</Badge>
                </div>
                <CardTitle className="text-lg">{module.title}</CardTitle>
                <CardDescription>{module.description}</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="flex items-center gap-2 text-xs text-muted-foreground"><Settings2 className="h-3.5 w-3.5" /> Backend contract and audit controls required</div>
              </CardContent>
            </Card>
          ))}
        </div>
      </div>
    </div>
  );
}
