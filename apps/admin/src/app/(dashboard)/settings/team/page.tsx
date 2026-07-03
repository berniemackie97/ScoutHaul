import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { TeamAccess } from "./team-access";

export const dynamic = "force-dynamic";

export default async function TeamAccessPage() {
  const api = await getApiClient();
  const { data: session } = await api.GET("/api/session/bootstrap", {});
  const orgId = session?.selectedOrganizationId ?? undefined;

  if (!orgId) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Team &amp; access</h1>
        <p className="text-muted-foreground text-sm">No active organization. Create or select one first.</p>
      </div>
    );
  }

  const [{ data: members }, { data: roles }, { data: permissions }, { data: codes }] = await Promise.all([
    api.GET("/api/organizations/{id}/members", { params: { path: { id: orgId } } }),
    api.GET("/api/organizations/{id}/roles", { params: { path: { id: orgId } } }),
    api.GET("/api/organizations/permissions", {}),
    api.GET("/api/organizations/{id}/invite-codes", { params: { path: { id: orgId } } }),
  ]);

  return (
    <div className="space-y-6">
      <div>
        <Link href="/settings" className="text-muted-foreground inline-flex items-center gap-1 text-sm hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Settings
        </Link>
        <h1 className="mt-2 text-2xl font-semibold tracking-tight">Team &amp; access</h1>
        <p className="text-muted-foreground mt-1 text-sm">Invite staff, assign roles, and control what each person can do.</p>
      </div>
      <TeamAccess
        orgId={orgId}
        members={members ?? []}
        roles={roles ?? []}
        permissions={permissions?.permissions ?? []}
        codes={codes ?? []}
      />
    </div>
  );
}
