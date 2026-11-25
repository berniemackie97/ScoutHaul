import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { PageHeader } from "@/components/list/list-search";
import { getApiClient } from "@/lib/api";
import { refusal } from "@/lib/page-access";
import { RegistrationForm } from "./registration-form";

export const dynamic = "force-dynamic";

export default async function SalesTaxSettingsPage() {
  const refused = await refusal("/tax/settings");
  if (refused) return refused;

  const api = await getApiClient();
  const { data: profile } = await api.GET("/api/sales-tax/profile");

  return (
    <div className="max-w-3xl space-y-6">
      <Link href="/tax" className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm">
        <ArrowLeft className="h-4 w-4" aria-hidden /> Sales tax
      </Link>
      <PageHeader
        eyebrow="Compliance"
        title="Florida registration"
        description="Copy these from your Certificate of Registration (DR-11) and your eServices account. They decide which returns are owed and what prints on each worksheet."
      />
      {profile ? (
        <RegistrationForm profile={profile} />
      ) : (
        <div className="border-destructive-border bg-destructive-surface text-destructive rounded-xl border p-6 text-sm">The registration could not be loaded.</div>
      )}
    </div>
  );
}
