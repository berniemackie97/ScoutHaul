"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";

type OrganizationRole = components["schemas"]["OrganizationRole"];

export type TeamResult = { ok: boolean; message: string; code?: string };

function text(formData: FormData, name: string): string | null {
  const value = formData.get(name);
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function errorMessage(error: unknown, fallback: string): string {
  if (typeof error === "string" && error.trim()) return error;
  if (error && typeof error === "object") {
    for (const key of ["detail", "title", "message"] as const) {
      const value = (error as Record<string, unknown>)[key];
      if (typeof value === "string" && value.trim()) return value;
    }
  }
  return fallback;
}

function revalidate() {
  revalidatePath("/settings/team");
}

export async function inviteMember(orgId: string, _prev: TeamResult, formData: FormData): Promise<TeamResult> {
  const email = text(formData, "email");
  const displayName = text(formData, "displayName");
  if (!email || !displayName) return { ok: false, message: "Name and email are required." };

  const api = await getApiClient();
  const result = await api.POST("/api/organizations/{id}/members", {
    params: { path: { id: orgId } },
    body: {
      email,
      displayName,
      role: (text(formData, "role") ?? "Member") as OrganizationRole,
      roleId: text(formData, "roleId"),
    },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The member could not be invited.") };
  revalidate();
  return { ok: true, message: `Invited ${displayName}.` };
}

export async function resendInvite(orgId: string, memberId: string): Promise<TeamResult> {
  const api = await getApiClient();
  const result = await api.POST("/api/organizations/{id}/members/{memberId}/resend-invite", {
    params: { path: { id: orgId, memberId } },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "Could not resend the invite.") };
  revalidate();
  return { ok: true, message: "Invite resent." };
}

export async function changeMemberRole(orgId: string, memberId: string, roleId: string | null, legacyRole: OrganizationRole | null): Promise<TeamResult> {
  const api = await getApiClient();
  const result = await api.PATCH("/api/organizations/{id}/members/{memberId}/role", {
    params: { path: { id: orgId, memberId } },
    body: { roleId, legacyRole: legacyRole ?? undefined },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "Could not change the role.") };
  revalidate();
  return { ok: true, message: "Role updated." };
}

export async function removeMember(orgId: string, memberId: string): Promise<TeamResult> {
  const api = await getApiClient();
  const result = (await api.DELETE("/api/organizations/{id}/members/{memberId}", {
    params: { path: { id: orgId, memberId } },
  })) as { error?: unknown };
  if (result.error) return { ok: false, message: errorMessage(result.error, "Could not remove the member.") };
  revalidate();
  return { ok: true, message: "Member removed." };
}

export async function createRole(orgId: string, _prev: TeamResult, formData: FormData): Promise<TeamResult> {
  const name = text(formData, "name");
  if (!name) return { ok: false, message: "Role name is required." };
  const permissions = formData.getAll("permissions").filter((value): value is string => typeof value === "string");

  const api = await getApiClient();
  const result = await api.POST("/api/organizations/{id}/roles", {
    params: { path: { id: orgId } },
    body: { name, description: text(formData, "description"), permissions, grantsOwnerAccess: formData.get("grantsOwnerAccess") === "on" },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The role could not be created.") };
  revalidate();
  return { ok: true, message: `Role “${name}” created.` };
}

export async function createInviteCode(orgId: string, _prev: TeamResult, formData: FormData): Promise<TeamResult> {
  const name = text(formData, "name");
  if (!name) return { ok: false, message: "Give the invite a label." };
  const maxUsesRaw = text(formData, "maxUses");
  const maxUses = maxUsesRaw ? Number(maxUsesRaw) : null;

  const api = await getApiClient();
  const result = await api.POST("/api/organizations/{id}/invite-codes", {
    params: { path: { id: orgId } },
    body: {
      name,
      teamId: null,
      roleId: text(formData, "roleId"),
      expiresAt: null,
      maxUses: maxUses && Number.isFinite(maxUses) && maxUses > 0 ? maxUses : null,
    },
  });
  if (!result.data?.code) return { ok: false, message: errorMessage(result.error, "The invite code could not be created.") };
  revalidate();
  return { ok: true, message: "Invite code created — copy it now, it won't be shown again.", code: result.data.code };
}

export async function deactivateInviteCode(orgId: string, inviteCodeId: string): Promise<TeamResult> {
  const api = await getApiClient();
  const result = await api.PATCH("/api/organizations/{id}/invite-codes/{inviteCodeId}/deactivate", {
    params: { path: { id: orgId, inviteCodeId } },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "Could not deactivate the code.") };
  revalidate();
  return { ok: true, message: "Invite code deactivated." };
}
