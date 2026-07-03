"use client";

import { useActionState, useState, useTransition } from "react";
import { useFormStatus } from "react-dom";
import { useRouter } from "next/navigation";
import { Copy, Mail, Shield, Trash2, UserPlus } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import {
  inviteMember, resendInvite, changeMemberRole, removeMember,
  createRole, createInviteCode, deactivateInviteCode, type TeamResult,
} from "./actions";

type Member = components["schemas"]["OrganizationMemberDto"];
type Role = components["schemas"]["OrganizationRoleDto"];
type InviteCode = components["schemas"]["OrganizationInviteCodeDto"];

const initial: TeamResult = { ok: false, message: "" };
const selectClass = "h-9 rounded-lg border border-input bg-background px-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring";
const date = (value: string | null | undefined) => (value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—");

function Submit({ label }: { label: string }) {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Working…" : label}</Button>;
}

export function TeamAccess({ orgId, members, roles, permissions, codes }: { orgId: string; members: Member[]; roles: Role[]; permissions: string[]; codes: InviteCode[] }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [rowMsg, setRowMsg] = useState<TeamResult | null>(null);

  const [inviteState, inviteAction] = useActionState(inviteMember.bind(null, orgId), initial);
  const [roleState, roleAction] = useActionState(createRole.bind(null, orgId), initial);
  const [codeState, codeAction] = useActionState(createInviteCode.bind(null, orgId), initial);

  function run(fn: () => Promise<TeamResult>) {
    startTransition(async () => {
      const outcome = await fn();
      setRowMsg(outcome);
      if (outcome.ok) router.refresh();
    });
  }

  return (
    <div className="space-y-6">
      {rowMsg && <p className={`text-sm ${rowMsg.ok ? "text-green-700" : "text-destructive"}`}>{rowMsg.message}</p>}

      {/* Members */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-lg"><UserPlus className="h-4 w-4" /> Members</CardTitle>
          <CardDescription>People with access to this organization.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="overflow-x-auto rounded-lg border">
            <table className="w-full text-sm">
              <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
                <tr><th className="px-3 py-2 font-medium">Member</th><th className="px-3 py-2 font-medium">Role</th><th className="px-3 py-2 font-medium">Status</th><th className="px-3 py-2 font-medium">Joined</th><th className="px-3 py-2" /></tr>
              </thead>
              <tbody className="divide-y">
                {members.map((member) => (
                  <tr key={member.id}>
                    <td className="px-3 py-2"><span className="font-medium">{member.displayName}</span><span className="text-muted-foreground block text-xs">{member.email}</span></td>
                    <td className="px-3 py-2">
                      <select
                        defaultValue={member.roleId ?? `legacy:${member.role}`}
                        disabled={pending || member.role === "Owner"}
                        onChange={(event) => {
                          const value = event.target.value;
                          const roleId = value.startsWith("legacy:") ? null : value;
                          const legacyRole = value.startsWith("legacy:") ? (value.slice(7) as "Owner" | "Member") : null;
                          run(() => changeMemberRole(orgId, member.id!, roleId, legacyRole));
                        }}
                        className={selectClass}
                      >
                        <option value="legacy:Owner">Owner</option>
                        <option value="legacy:Member">Member</option>
                        {roles.filter((role) => !role.isOwnerRole).map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}
                      </select>
                    </td>
                    <td className="px-3 py-2"><Badge variant={member.inviteStatus === "Active" ? "success" : "warning"}>{member.inviteStatus}</Badge></td>
                    <td className="text-muted-foreground px-3 py-2">{date(member.joinedAt)}</td>
                    <td className="px-3 py-2 text-right">
                      <div className="flex justify-end gap-1">
                        {member.inviteStatus !== "Active" && (
                          <button type="button" disabled={pending} onClick={() => run(() => resendInvite(orgId, member.id!))} className="hover:bg-muted rounded p-1.5" title="Resend invite"><Mail className="h-4 w-4" /></button>
                        )}
                        {member.role !== "Owner" && (
                          <button type="button" disabled={pending} onClick={() => { if (confirm(`Remove ${member.displayName}?`)) run(() => removeMember(orgId, member.id!)); }} className="text-muted-foreground hover:text-destructive rounded p-1.5" title="Remove"><Trash2 className="h-4 w-4" /></button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <form action={inviteAction} className="grid gap-2 rounded-lg border bg-muted/20 p-3 sm:grid-cols-[1fr_1fr_160px_auto]">
            <Input name="displayName" placeholder="Full name" required className="h-9" />
            <Input name="email" type="email" placeholder="email@example.com" required className="h-9" />
            <select name="roleId" className={selectClass} defaultValue="">
              <option value="">Member (base)</option>
              {roles.filter((role) => !role.isOwnerRole).map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}
            </select>
            <Submit label="Invite" />
            {inviteState.message && <p className={`text-xs sm:col-span-4 ${inviteState.ok ? "text-green-700" : "text-destructive"}`}>{inviteState.message}</p>}
          </form>
        </CardContent>
      </Card>

      {/* Roles */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-lg"><Shield className="h-4 w-4" /> Roles &amp; permissions</CardTitle>
          <CardDescription>Custom roles bundle permissions. Owners always have every permission.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-2 sm:grid-cols-2">
            {roles.map((role) => (
              <div key={role.id} className="rounded-lg border p-3 text-sm">
                <div className="flex items-center gap-2"><span className="font-medium">{role.name}</span>{role.isOwnerRole && <Badge variant="success">Owner</Badge>}{role.isSystem && <Badge variant="secondary">System</Badge>}</div>
                <p className="text-muted-foreground mt-1 text-xs">{role.description || `${role.permissions?.length ?? 0} permissions`}</p>
              </div>
            ))}
          </div>

          <details className="rounded-lg border bg-muted/20 p-3">
            <summary className="cursor-pointer text-sm font-medium">Create a role</summary>
            <form action={roleAction} className="mt-3 space-y-3">
              <div className="grid gap-2 sm:grid-cols-2">
                <div className="space-y-1"><Label htmlFor="name" className="text-xs">Role name</Label><Input id="name" name="name" required className="h-9" placeholder="Lister" /></div>
                <div className="space-y-1"><Label htmlFor="description" className="text-xs">Description</Label><Input id="description" name="description" className="h-9" /></div>
              </div>
              <div className="space-y-1">
                <Label className="text-xs">Permissions</Label>
                <div className="grid gap-1 sm:grid-cols-2 md:grid-cols-3">
                  {permissions.map((permission) => (
                    <label key={permission} className="flex items-center gap-2 text-xs"><input type="checkbox" name="permissions" value={permission} /> {permission}</label>
                  ))}
                </div>
              </div>
              <label className="flex items-center gap-2 text-xs"><input type="checkbox" name="grantsOwnerAccess" /> Grants full owner access</label>
              <div className="flex items-center gap-3"><Submit label="Create role" />{roleState.message && <span className={`text-xs ${roleState.ok ? "text-green-700" : "text-destructive"}`}>{roleState.message}</span>}</div>
            </form>
          </details>
        </CardContent>
      </Card>

      {/* Invite codes */}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Invite codes</CardTitle>
          <CardDescription>Shareable codes people redeem to join with a preset role.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {codes.length > 0 && (
            <div className="space-y-2">
              {codes.map((code) => (
                <div key={code.id} className="flex items-center justify-between rounded-lg border p-2 text-sm">
                  <div><span className="font-medium">{code.name}</span> <span className="text-muted-foreground font-mono text-xs">{code.codePrefix}…</span> <span className="text-muted-foreground text-xs">· {code.useCount}{code.maxUses ? `/${code.maxUses}` : ""} used</span></div>
                  <div className="flex items-center gap-2">{code.isActive ? <Badge variant="success">Active</Badge> : <Badge variant="outline">Inactive</Badge>}
                    {code.isActive && <button type="button" disabled={pending} onClick={() => run(() => deactivateInviteCode(orgId, code.id!))} className="text-muted-foreground hover:text-destructive text-xs">Deactivate</button>}
                  </div>
                </div>
              ))}
            </div>
          )}
          {codeState.code && (
            <div className="flex items-center justify-between rounded-lg border border-green-300 bg-green-50 p-2 text-sm">
              <span className="font-mono">{codeState.code}</span>
              <button type="button" onClick={() => navigator.clipboard?.writeText(codeState.code!)} className="text-green-800 inline-flex items-center gap-1 text-xs"><Copy className="h-3 w-3" /> Copy</button>
            </div>
          )}
          <form action={codeAction} className="grid gap-2 rounded-lg border bg-muted/20 p-3 sm:grid-cols-[1fr_160px_120px_auto]">
            <Input name="name" placeholder="Label (e.g. Warehouse crew)" required className="h-9" />
            <select name="roleId" className={selectClass} defaultValue=""><option value="">Member (base)</option>{roles.filter((role) => !role.isOwnerRole).map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}</select>
            <Input name="maxUses" type="number" min="1" placeholder="Max uses" className="h-9" />
            <Submit label="Create code" />
            {codeState.message && !codeState.code && <p className={`text-xs sm:col-span-4 ${codeState.ok ? "text-green-700" : "text-destructive"}`}>{codeState.message}</p>}
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
