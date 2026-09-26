"use client";

import { useActionState, useEffect, useState, useTransition } from "react";
import { useFormStatus } from "react-dom";
import { useRouter } from "next/navigation";
import { Copy, Mail, Pencil, Shield, Trash2, UserPlus, X } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, formatDate } from "@overhaul/ui";
import { permissionLabel } from "@/lib/permissions";
import {
  inviteMember, resendInvite, changeMemberRole, removeMember,
  createRole, updateRole, createInviteCode, deactivateInviteCode, type TeamResult,
} from "./actions";

type Member = components["schemas"]["OrganizationMemberDto"];
type Role = components["schemas"]["OrganizationRoleDto"];
type InviteCode = components["schemas"]["OrganizationInviteCodeDto"];

const initial: TeamResult = { ok: false, message: "" };
const selectClass = "h-9 rounded-lg border border-input bg-background px-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring";
const date = (value: string | null | undefined) => (formatDate(value));

function Submit({ label }: { label: string }) {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Working…" : label}</Button>;
}

function FieldError({ message }: { message?: string }) {
  return message ? <p className="text-destructive text-xs">{message}</p> : null;
}

export function TeamAccess({ orgId, members, roles, permissions, codes }: { orgId: string; members: Member[]; roles: Role[]; permissions: string[]; codes: InviteCode[] }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [rowMsg, setRowMsg] = useState<TeamResult | null>(null);

  const [inviteState, inviteAction] = useActionState(inviteMember.bind(null, orgId), initial);
  const [roleState, roleAction] = useActionState(createRole.bind(null, orgId), initial);
  const [codeState, codeAction] = useActionState(createInviteCode.bind(null, orgId), initial);
  const inviteValues = inviteState.values ?? {};
  const inviteErrors = inviteState.fieldErrors ?? {};
  const inviteKey = inviteState.formKey ?? "initial";
  const roleValues = roleState.values ?? {};
  const roleErrors = roleState.fieldErrors ?? {};
  const roleKey = roleState.formKey ?? "initial";
  const selectedPermissions = new Set((roleValues.selectedPermissions ?? "").split("|").filter(Boolean));
  const codeValues = codeState.values ?? {};
  const codeErrors = codeState.fieldErrors ?? {};
  const codeKey = codeState.formKey ?? "initial";
  const ownerCount = members.filter((member) => member.role === "Owner").length;

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
                        defaultValue={member.role === "Owner" ? "legacy:Owner" : (member.roleId ?? `legacy:${member.role}`)}
                        disabled={pending}
                        onChange={(event) => {
                          const value = event.target.value;
                          const current = member.role === "Owner" ? "legacy:Owner" : (member.roleId ?? `legacy:${member.role}`);
                          const revert = () => { event.target.value = current; };
                          // Never strand the org without an owner — promote a replacement first.
                          if (member.role === "Owner" && value !== "legacy:Owner" && ownerCount <= 1) {
                            setRowMsg({ ok: false, message: "This is the only owner. Make another member an Owner first, then change this one." });
                            revert();
                            return;
                          }
                          if (value === "legacy:Owner" && member.role !== "Owner" &&
                              !confirm(`Make ${member.displayName} an Owner? Owners get full control of the organization.`)) {
                            revert();
                            return;
                          }
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
            <div className="space-y-1">
              <Input
                key={`displayName:${inviteKey}`}
                name="displayName"
                placeholder="Full name"
                required
                className="h-9"
                defaultValue={inviteValues.displayName ?? ""}
                aria-invalid={inviteErrors.displayName ? "true" : undefined}
              />
              <FieldError message={inviteErrors.displayName} />
            </div>
            <div className="space-y-1">
              <Input
                key={`email:${inviteKey}`}
                name="email"
                type="email"
                placeholder="email@example.com"
                required
                className="h-9"
                defaultValue={inviteValues.email ?? ""}
                aria-invalid={inviteErrors.email ? "true" : undefined}
              />
              <FieldError message={inviteErrors.email} />
            </div>
            <div className="space-y-1">
              <select key={`roleId:${inviteKey}`} name="roleId" className={selectClass} defaultValue={inviteValues.roleId ?? ""}>
                <option value="">Member (base)</option>
                {roles.filter((role) => !role.isOwnerRole).map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}
              </select>
              <FieldError message={inviteErrors.roleId} />
            </div>
            <div className="self-start"><Submit label="Invite" /></div>
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
              <RoleCard key={role.id} orgId={orgId} role={role} permissions={permissions} onDone={() => router.refresh()} />
            ))}
          </div>

          <details className="rounded-lg border bg-muted/20 p-3" open={Boolean(roleState.message && !roleState.ok)}>
            <summary className="cursor-pointer text-sm font-medium">Create a role</summary>
            <form action={roleAction} className="mt-3 space-y-3">
              <div className="grid gap-2 sm:grid-cols-2">
                <div className="space-y-1">
                  <Label htmlFor="name" className="text-xs">Role name</Label>
                  <Input
                    key={`role-name:${roleKey}`}
                    id="name"
                    name="name"
                    required
                    className="h-9"
                    placeholder="Lister"
                    defaultValue={roleValues.name ?? ""}
                    aria-invalid={roleErrors.name ? "true" : undefined}
                  />
                  <FieldError message={roleErrors.name} />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="description" className="text-xs">Description</Label>
                  <Input
                    key={`role-description:${roleKey}`}
                    id="description"
                    name="description"
                    className="h-9"
                    defaultValue={roleValues.description ?? ""}
                  />
                </div>
              </div>
              <div className="space-y-1">
                <Label className="text-xs">Permissions</Label>
                <div className="grid gap-1 sm:grid-cols-2 md:grid-cols-3">
                  {permissions.map((permission) => (
                    <label key={permission} className="flex items-center gap-2 text-xs">
                      <input
                        key={`${permission}:${roleKey}`}
                        type="checkbox"
                        name="permissions"
                        value={permission}
                        defaultChecked={selectedPermissions.has(permission)}
                      />{" "}
                      {permissionLabel(permission)}
                    </label>
                  ))}
                </div>
                <FieldError message={roleErrors.permissions} />
              </div>
              <p className="text-muted-foreground text-xs">Owner-level access stays system-governed. Custom roles must grant explicit permissions.</p>
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
            <div className="space-y-1">
              <Input
                key={`code-name:${codeKey}`}
                name="name"
                placeholder="Label (e.g. Warehouse crew)"
                required
                className="h-9"
                defaultValue={codeValues.name ?? ""}
                aria-invalid={codeErrors.name ? "true" : undefined}
              />
              <FieldError message={codeErrors.name} />
            </div>
            <div className="space-y-1">
              <select key={`code-role:${codeKey}`} name="roleId" className={selectClass} defaultValue={codeValues.roleId ?? ""}>
                <option value="">Member (base)</option>
                {roles.filter((role) => !role.isOwnerRole).map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}
              </select>
              <FieldError message={codeErrors.roleId} />
            </div>
            <div className="space-y-1">
              <Input
                key={`code-maxUses:${codeKey}`}
                name="maxUses"
                type="number"
                min="1"
                step="1"
                placeholder="Max uses"
                className="h-9"
                defaultValue={codeValues.maxUses ?? ""}
                aria-invalid={codeErrors.maxUses ? "true" : undefined}
              />
              <FieldError message={codeErrors.maxUses} />
            </div>
            <div className="self-start"><Submit label="Create code" /></div>
            {codeState.message && !codeState.code && <p className={`text-xs sm:col-span-4 ${codeState.ok ? "text-green-700" : "text-destructive"}`}>{codeState.message}</p>}
          </form>
        </CardContent>
      </Card>
    </div>
  );
}

function RoleCard({ orgId, role, permissions, onDone }: { orgId: string; role: Role; permissions: string[]; onDone: () => void }) {
  const [editing, setEditing] = useState(false);
  const [state, action] = useActionState(updateRole.bind(null, orgId, role.id!), initial);
  const errors = state.fieldErrors ?? {};
  const editable = !role.isSystem && !role.isOwnerRole;
  const rolePerms = new Set(role.permissions ?? []);

  useEffect(() => {
    if (state.ok) {
      setEditing(false);
      onDone();
    }
  }, [state.ok, onDone]);

  if (!editing) {
    return (
      <div className="rounded-lg border p-3 text-sm">
        <div className="flex items-center gap-2">
          <span className="font-medium">{role.name}</span>
          {role.isOwnerRole && <Badge variant="success">Owner</Badge>}
          {role.isSystem && <Badge variant="secondary">System</Badge>}
          {editable && (
            <button type="button" onClick={() => setEditing(true)} className="hover:bg-muted text-muted-foreground ml-auto rounded p-1" title="Edit role">
              <Pencil className="h-3.5 w-3.5" />
            </button>
          )}
        </div>
        <p className="text-muted-foreground mt-1 text-xs">{role.description || `${role.permissions?.length ?? 0} permissions`}</p>
      </div>
    );
  }

  return (
    <form action={action} className="space-y-3 rounded-lg border border-ring/50 p-3 text-sm sm:col-span-2">
      <div className="flex items-center justify-between">
        <span className="font-medium">Edit “{role.name}”</span>
        <button type="button" onClick={() => setEditing(false)} className="hover:bg-muted text-muted-foreground rounded p-1" title="Cancel"><X className="h-4 w-4" /></button>
      </div>
      <div className="grid gap-2 sm:grid-cols-2">
        <div className="space-y-1">
          <Label htmlFor={`name-${role.id}`} className="text-xs">Role name</Label>
          <Input id={`name-${role.id}`} name="name" required className="h-9" defaultValue={role.name ?? ""} aria-invalid={errors.name ? "true" : undefined} />
          <FieldError message={errors.name} />
        </div>
        <div className="space-y-1">
          <Label htmlFor={`desc-${role.id}`} className="text-xs">Description</Label>
          <Input id={`desc-${role.id}`} name="description" className="h-9" defaultValue={role.description ?? ""} />
        </div>
      </div>
      <div className="space-y-1">
        <Label className="text-xs">Permissions</Label>
        <div className="grid gap-1 sm:grid-cols-2 md:grid-cols-3">
          {permissions.map((permission) => (
            <label key={permission} className="flex items-center gap-2 text-xs">
              <input type="checkbox" name="permissions" value={permission} defaultChecked={rolePerms.has(permission)} /> {permissionLabel(permission)}
            </label>
          ))}
        </div>
        <FieldError message={errors.permissions} />
      </div>
      <div className="flex items-center gap-3">
        <Submit label="Save changes" />
        {state.message && <span className={`text-xs ${state.ok ? "text-green-700" : "text-destructive"}`}>{state.message}</span>}
      </div>
    </form>
  );
}
