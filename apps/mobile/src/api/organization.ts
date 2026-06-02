import { apiRequest } from './client';
import type {
  Organization,
  OrganizationInviteCode,
  OrganizationInviteCodeCreated,
  OrganizationJoinResult,
  OrganizationMember,
  OrganizationRoleDefinition,
  OrganizationTeam,
  OrganizationTeamMember,
  SessionBootstrap,
} from '../domain/types';

export type CreateOrganizationRequest = { name: string };

export type AddMemberRequest = {
  email: string;
  displayName?: string;
  role?: string;
  roleId?: string | null;
};

export type CreateRoleRequest = {
  name: string;
  description?: string | null;
  permissions: string[];
};

export type UpdateMemberRoleRequest = {
  roleId: string | null;
  role?: string;
  legacyRole?: string;
};

export type CreateTeamRequest = {
  name: string;
  description?: string | null;
};

export type AddTeamMemberRequest = {
  userId: string;
  roleId?: string | null;
};

export type CreateInviteCodeRequest = {
  name: string;
  teamId?: string | null;
  roleId?: string | null;
  maxUses?: number | null;
  expiresAt?: string | null;
};

export const organizationApi = {
  bootstrapSession: () => apiRequest<SessionBootstrap>('/api/session/bootstrap'),
  list: () => apiRequest<Organization[]>('/api/organizations'),
  create: (body: CreateOrganizationRequest) => apiRequest<Organization>('/api/organizations', { method: 'POST', body }),

  members: (orgId: string) => apiRequest<OrganizationMember[]>(`/api/organizations/${orgId}/members`),
  addMember: (orgId: string, body: AddMemberRequest) =>
    apiRequest<OrganizationMember>(`/api/organizations/${orgId}/members`, { method: 'POST', body }),
  resendInvite: (orgId: string, memberId: string) =>
    apiRequest<OrganizationMember>(`/api/organizations/${orgId}/members/${memberId}/resend-invite`, { method: 'POST' }),
  removeMember: (orgId: string, memberId: string) =>
    apiRequest<void>(`/api/organizations/${orgId}/members/${memberId}`, { method: 'DELETE' }),
  updateMemberRole: (orgId: string, memberId: string, body: UpdateMemberRoleRequest) =>
    apiRequest<OrganizationMember>(`/api/organizations/${orgId}/members/${memberId}/role`, { method: 'PATCH', body }),

  permissions: () => apiRequest<{ permissions: string[] }>('/api/organizations/permissions'),
  roles: (orgId: string) => apiRequest<OrganizationRoleDefinition[]>(`/api/organizations/${orgId}/roles`),
  createRole: (orgId: string, body: CreateRoleRequest) =>
    apiRequest<OrganizationRoleDefinition>(`/api/organizations/${orgId}/roles`, { method: 'POST', body }),
  updateRole: (orgId: string, roleId: string, body: CreateRoleRequest) =>
    apiRequest<OrganizationRoleDefinition>(`/api/organizations/${orgId}/roles/${roleId}`, { method: 'PUT', body }),
  deleteRole: (orgId: string, roleId: string) =>
    apiRequest<void>(`/api/organizations/${orgId}/roles/${roleId}`, { method: 'DELETE' }),

  teams: (orgId: string) => apiRequest<OrganizationTeam[]>(`/api/organizations/${orgId}/teams`),
  createTeam: (orgId: string, body: CreateTeamRequest) =>
    apiRequest<OrganizationTeam>(`/api/organizations/${orgId}/teams`, { method: 'POST', body }),
  updateTeam: (orgId: string, teamId: string, body: CreateTeamRequest) =>
    apiRequest<OrganizationTeam>(`/api/organizations/${orgId}/teams/${teamId}`, { method: 'PUT', body }),
  deleteTeam: (orgId: string, teamId: string) =>
    apiRequest<void>(`/api/organizations/${orgId}/teams/${teamId}`, { method: 'DELETE' }),

  teamMembers: (orgId: string, teamId: string) =>
    apiRequest<OrganizationTeamMember[]>(`/api/organizations/${orgId}/teams/${teamId}/members`),
  addTeamMember: (orgId: string, teamId: string, body: AddTeamMemberRequest) =>
    apiRequest<OrganizationTeamMember>(`/api/organizations/${orgId}/teams/${teamId}/members`, { method: 'POST', body }),
  removeTeamMember: (orgId: string, teamId: string, memberId: string) =>
    apiRequest<void>(`/api/organizations/${orgId}/teams/${teamId}/members/${memberId}`, { method: 'DELETE' }),

  inviteCodes: (orgId: string) =>
    apiRequest<OrganizationInviteCode[]>(`/api/organizations/${orgId}/invite-codes`),
  createInviteCode: (orgId: string, body: CreateInviteCodeRequest) =>
    apiRequest<OrganizationInviteCodeCreated>(`/api/organizations/${orgId}/invite-codes`, { method: 'POST', body }),
  deactivateInviteCode: (orgId: string, inviteCodeId: string) =>
    apiRequest<OrganizationInviteCode>(`/api/organizations/${orgId}/invite-codes/${inviteCodeId}/deactivate`, { method: 'PATCH' }),
  redeemInviteCode: (code: string) =>
    apiRequest<OrganizationJoinResult>('/api/organizations/invite-codes/redeem', { method: 'POST', body: { code } }),
};
