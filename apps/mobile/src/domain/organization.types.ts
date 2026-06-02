export type OrganizationRole = 'Owner' | 'Admin' | 'Member' | 'Viewer';

export type Organization = {
  id: string;
  name: string;
  createdAt: string;
};

export type OrganizationRoleDefinition = {
  id: string;
  name: string;
  description?: string | null;
  isSystem: boolean;
  isOwnerRole: boolean;
  permissions: string[];
};

export type OrganizationMember = {
  id: string;
  userId: string;
  email: string;
  displayName: string;
  role: OrganizationRole;
  roleId?: string | null;
  roleName?: string | null;
  inviteStatus: 'Pending' | 'Active' | string;
  joinedAt: string;
};

export type OrganizationTeam = {
  id: string;
  organizationId: string;
  name: string;
  description?: string | null;
  memberCount: number;
  createdAt: string;
};

export type OrganizationTeamMember = {
  id: string;
  teamId: string;
  userId: string;
  email: string;
  displayName: string;
  roleId?: string | null;
  roleName?: string | null;
  joinedAt: string;
};

export type OrganizationInviteCode = {
  id: string;
  organizationId: string;
  teamId?: string | null;
  teamName?: string | null;
  roleId?: string | null;
  roleName?: string | null;
  name: string;
  codePrefix: string;
  isActive: boolean;
  expiresAt?: string | null;
  maxUses?: number | null;
  useCount: number;
  createdAt: string;
};

export type OrganizationInviteCodeCreated = {
  invite: OrganizationInviteCode;
  code: string;
};

export type OrganizationJoinResult = {
  organization: Organization;
  member: OrganizationMember;
  teamId?: string | null;
};

export type SessionBootstrap = {
  user: {
    id: string;
    email: string;
    displayName: string;
  };
  organizations: Organization[];
  selectedOrganizationId: string;
  remoteCounts: {
    inventoryItems: number;
    budgets: number;
    marketObservations: number;
    listings: number;
    tasks: number;
    pendingSyncOperations: number;
  };
  serverTime: string;
};
