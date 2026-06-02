import { getDatabase } from './database';

export async function getPreference<T>(key: string): Promise<T | null> {
  const db = await getDatabase();
  const row = await db.getFirstAsync<{ valueJson: string }>(
    `select valueJson from app_preferences where key = ?`,
    [key],
  );
  return row ? JSON.parse(row.valueJson) as T : null;
}

export async function setPreference(key: string, value: unknown): Promise<void> {
  const db = await getDatabase();
  await db.runAsync(
    `insert into app_preferences (key, valueJson, updatedAt) values (?, ?, ?)
    on conflict(key) do update set
      valueJson = excluded.valueJson,
      updatedAt = excluded.updatedAt`,
    [key, JSON.stringify(value), new Date().toISOString()],
  );
}

export type WorkspaceUsageMode = 'solo' | 'join' | 'team';
export type WorkspacePlanIntent = 'starter' | 'pro' | 'team';
export type AccountMode = 'local' | 'signedOut' | 'signedIn';

export type AppWorkspaceProfile = {
  onboardingCompleted: boolean;
  onboardingSkipped: boolean;
  workspaceName: string;
  usageMode: WorkspaceUsageMode;
  role: 'owner' | 'operator' | 'employee';
  monthlySourcingBudget?: number | null;
  primaryGoal: 'source' | 'inventory' | 'listing' | 'profit';
  planIntent: WorkspacePlanIntent;
  accountMode: AccountMode;
  accountEmail?: string | null;
  displayName?: string | null;
  pendingInviteCode?: string | null;
  joinedOrganizationId?: string | null;
  joinedTeamId?: string | null;
  analyticsConsent: boolean;
  marketingConsent: boolean;
  createdAt: string;
  updatedAt: string;
};

export async function getWorkspaceProfile(): Promise<AppWorkspaceProfile | null> {
  return getPreference<AppWorkspaceProfile>('workspaceProfile');
}

export async function hasCompletedOnboarding(): Promise<boolean> {
  const profile = await getWorkspaceProfile();
  return profile?.onboardingCompleted === true;
}

function hasOwn<T extends object>(value: T, key: PropertyKey): boolean {
  return Object.prototype.hasOwnProperty.call(value, key);
}

export async function saveWorkspaceProfile(
  input: Omit<Partial<AppWorkspaceProfile>, 'createdAt' | 'updatedAt'>,
): Promise<AppWorkspaceProfile> {
  const existing = await getWorkspaceProfile();
  const now = new Date().toISOString();
  const profile: AppWorkspaceProfile = {
    onboardingCompleted: input.onboardingCompleted ?? true,
    onboardingSkipped: input.onboardingSkipped ?? false,
    workspaceName: input.workspaceName?.trim() || existing?.workspaceName || 'My resale workspace',
    usageMode: input.usageMode ?? existing?.usageMode ?? 'solo',
    role: input.role ?? existing?.role ?? 'owner',
    monthlySourcingBudget: input.monthlySourcingBudget ?? existing?.monthlySourcingBudget ?? null,
    primaryGoal: input.primaryGoal ?? existing?.primaryGoal ?? 'source',
    planIntent: input.planIntent ?? existing?.planIntent ?? 'starter',
    accountMode: input.accountMode ?? existing?.accountMode ?? 'local',
    accountEmail: input.accountEmail ?? existing?.accountEmail ?? null,
    displayName: input.displayName ?? existing?.displayName ?? null,
    pendingInviteCode: hasOwn(input, 'pendingInviteCode') ? input.pendingInviteCode ?? null : existing?.pendingInviteCode ?? null,
    joinedOrganizationId: hasOwn(input, 'joinedOrganizationId') ? input.joinedOrganizationId ?? null : existing?.joinedOrganizationId ?? null,
    joinedTeamId: hasOwn(input, 'joinedTeamId') ? input.joinedTeamId ?? null : existing?.joinedTeamId ?? null,
    analyticsConsent: input.analyticsConsent ?? existing?.analyticsConsent ?? false,
    marketingConsent: input.marketingConsent ?? existing?.marketingConsent ?? false,
    createdAt: existing?.createdAt ?? now,
    updatedAt: now,
  };

  await setPreference('workspaceProfile', profile);
  return profile;
}

export type BudgetPlanBreakdown = {
  inventory: number;
  fuel: number;
  supplies: number;
  shipping: number;
  fees: number;
  reserve: number;
  updatedAt: string;
};

export async function getBudgetPlanBreakdown(): Promise<BudgetPlanBreakdown | null> {
  return getPreference<BudgetPlanBreakdown>('budgetPlanBreakdown');
}

export async function saveBudgetPlanBreakdown(
  input: Omit<BudgetPlanBreakdown, 'updatedAt'>,
): Promise<BudgetPlanBreakdown> {
  const plan = { ...input, updatedAt: new Date().toISOString() };
  await setPreference('budgetPlanBreakdown', plan);
  return plan;
}
