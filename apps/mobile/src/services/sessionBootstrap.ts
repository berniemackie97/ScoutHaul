import { api } from '../api';
import { getWorkspaceProfile, saveWorkspaceProfile } from '../db/localStore';
import type { SessionBootstrap } from '../domain/types';
import { authStore } from './auth';
import { setActiveOrganizationId } from './workspaceSession';

export async function bootstrapSignedInWorkspace(): Promise<SessionBootstrap | null> {
  if (!authStore.isAuthenticated) {
    return null;
  }

  let bootstrap: SessionBootstrap;
  try {
    bootstrap = await api.bootstrapSession();
  } catch {
    await setActiveOrganizationId(null);
    bootstrap = await api.bootstrapSession();
  }

  await setActiveOrganizationId(bootstrap.selectedOrganizationId);
  const existing = await getWorkspaceProfile();
  await saveWorkspaceProfile({
    onboardingCompleted: true,
    accountMode: 'signedIn',
    accountEmail: bootstrap.user.email,
    displayName: bootstrap.user.displayName,
    workspaceName: existing?.workspaceName || bootstrap.organizations[0]?.name || 'My resale workspace',
    usageMode: existing?.usageMode ?? 'solo',
    joinedOrganizationId: bootstrap.selectedOrganizationId,
  });

  return bootstrap;
}
