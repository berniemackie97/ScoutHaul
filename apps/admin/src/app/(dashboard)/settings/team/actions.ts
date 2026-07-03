"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { failedFormState, type FailedFormState, type FieldErrors } from "@/lib/form-state";

type OrganizationRole = components["schemas"]["OrganizationRole"];

export type TeamResult = { ok: boolean; message: string; code?: string } & FailedFormState;

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

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

function selectedPermissions(formData: FormData) {
  return formData.getAll("permissions").filter((value): value is string => typeof value === "string" && value.length > 0);
}

function failure(message: string, formData: FormData, fieldErrors: FieldErrors = {}): TeamResult {
  const result = failedFormState(message, formData, fieldErrors);
  return {
    ...result,
    values: {
      ...(result.values ?? {}),
      selectedPermissions: selectedPermissions(formData).join("|"),
    },
  };
}

function revalidate() {
  revalidatePath("/settings/team");
}

export async function inviteMember(orgId: string, _prev: TeamResult, formData: FormData): Promise<TeamResult> {
  const email = text(formData, "email");
  const displayName = text(formData, "displayName");
  const fieldErrors: FieldErrors = {};
  if (!displayName) fieldErrors.displayName = "Name is required.";
  if (!email) fieldErrors.email = "Email is required.";
  else if (!emailPattern.test(email)) fieldErrors.email = "Enter a valid email address.";
  if (Object.keys(fieldErrors).length > 0) {
    return failure("Fix the highlighted fields.", formData, fieldErrors);
  }

  const api = await getApiClient();
  const result = await api.POST("/api/organizations/{id}/members", {
    params: { path: { id: orgId } },
    body: {
      email: email!,
      displayName: displayName!,
      role: (text(formData, "role") ?? "Member") as OrganizationRole,
      roleId: text(formData, "roleId"),
    },
  });
  if (!result.data) return failure(errorMessage(result.error, "The member could not be invited."), formData);
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
  const permissions = selectedPermissions(formData);
  const fieldErrors: FieldErrors = {};
  if (!name) fieldErrors.name = "Role name is required.";
  if (permissions.length === 0) fieldErrors.permissions = "Select at least one permission for a custom role.";
  if (formData.get("grantsOwnerAccess") === "on") {
    fieldErrors.permissions = "Owner access is system-governed and cannot be granted by a custom role from this screen.";
  }
  if (Object.keys(fieldErrors).length > 0) {
    return failure("Fix the highlighted fields.", formData, fieldErrors);
  }

  const api = await getApiClient();
  const result = await api.POST("/api/organizations/{id}/roles", {
    params: { path: { id: orgId } },
    body: { name: name!, description: text(formData, "description"), permissions, grantsOwnerAccess: false },
  });
  if (!result.data) return failure(errorMessage(result.error, "The role could not be created."), formData);
  revalidate();
  return { ok: true, message: `Role “${name}” created.` };
}

export async function createInviteCode(orgId: string, _prev: TeamResult, formData: FormData): Promise<TeamResult> {
  const name = text(formData, "name");
  const fieldErrors: FieldErrors = {};
  if (!name) fieldErrors.name = "Give the invite a label.";
  const maxUsesRaw = text(formData, "maxUses");
  let maxUses: number | null = null;
  if (maxUsesRaw) {
    const parsedMaxUses = Number(maxUsesRaw);
    if (!Number.isInteger(parsedMaxUses) || parsedMaxUses <= 0) {
      fieldErrors.maxUses = "Max uses must be a positive whole number.";
    } else {
      maxUses = parsedMaxUses;
    }
  }
  if (Object.keys(fieldErrors).length > 0) {
    return failure("Fix the highlighted fields.", formData, fieldErrors);
  }

  const api = await getApiClient();
  const result = await api.POST("/api/organizations/{id}/invite-codes", {
    params: { path: { id: orgId } },
    body: {
      name: name!,
      teamId: null,
      roleId: text(formData, "roleId"),
      expiresAt: null,
      maxUses,
    },
  });
  if (!result.data?.code) return failure(errorMessage(result.error, "The invite code could not be created."), formData);
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
