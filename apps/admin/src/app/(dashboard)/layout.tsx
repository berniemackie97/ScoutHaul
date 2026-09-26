import { DashboardShell } from "@/components/layout/dashboard-shell";
import { getSession, operatorFrom } from "@/lib/session";

export default async function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  // Cached per request, so the home page's own read of the session shares this one call.
  const session = await getSession();
  return (
    <DashboardShell operator={operatorFrom(session)} permissions={session ? (session.permissions ?? []) : null}>
      {children}
    </DashboardShell>
  );
}
