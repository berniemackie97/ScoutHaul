import { Badge, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import { Bell, Building2, CreditCard, LockKeyhole, ReceiptText, Settings2, Truck } from "lucide-react";

const modules = [
  { title: "Business and storefront", description: "Legal identity, storefront branding, contact details, domains, policies, and document numbering.", icon: Building2 },
  { title: "Payments and checkout", description: "Payment processors, accepted methods, capture rules, refunds, fraud controls, and webhook health.", icon: CreditCard },
  { title: "Shipping and fulfillment", description: "Warehouses, carriers, service levels, packaging, label defaults, local pickup, and return routing.", icon: Truck },
  { title: "Tax and accounting", description: "Tax registrations, calculation provider, reporting basis, fiscal periods, account mappings, and exports.", icon: ReceiptText },
  { title: "Notifications", description: "Customer and operator templates, delivery channels, event routing, escalation, and quiet hours.", icon: Bell },
  { title: "Access and governance", description: "Staff, roles, permissions, approvals, sessions, audit retention, integrations, and API credentials.", icon: LockKeyhole },
];

export default function SettingsPage() {
  return (
    <div className="space-y-6">
      <header>
        <p className="text-sm text-muted-foreground">System</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Settings and governance</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">The control plane for store behavior, financial policy, integrations, security, and staff access.</p>
      </header>

      <div className="rounded-xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-950">
        Configuration persistence is not enabled yet. No settings shown here pretend to save; each module will ship with validated backend storage, permissions, and audit history.
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {modules.map((module) => (
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
  );
}
