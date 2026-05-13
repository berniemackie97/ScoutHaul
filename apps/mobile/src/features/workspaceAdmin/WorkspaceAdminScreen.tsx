import { useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Divider } from '../../components/Divider';
import { Screen } from '../../components/Screen';
import { SectionHeader } from '../../components/SectionHeader';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import { radius, spacing } from '../../components/theme';
import { useColors } from '../../context/ThemeContext';
import type { Organization, OrganizationMember, OrganizationRoleDefinition, OrganizationTeam } from '../../domain/types';
import { getActiveOrganizationId, setActiveOrganizationId } from '../../services/workspaceSession';
import { MemberManager } from './MemberManager';
import { RoleManager } from './RoleManager';
import { TeamManager } from './TeamManager';

type WorkspaceData = {
  organizations: Organization[];
  permissions: string[];
  members: OrganizationMember[];
  roles: OrganizationRoleDefinition[];
  teams: OrganizationTeam[];
};

export function WorkspaceAdminScreen() {
  const c = useColors();
  const [data, setData] = useState<WorkspaceData>({
    organizations: [],
    permissions: [],
    members: [],
    roles: [],
    teams: [],
  });
  const [selectedOrganizationId, setSelectedOrganizationId] = useState<string | null>(null);
  const [organizationName, setOrganizationName] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isCreatingOrg, setIsCreatingOrg] = useState(false);

  const selectedOrganization = data.organizations.find((organization) => organization.id === selectedOrganizationId) ?? null;

  const load = useCallback(async () => {
    setIsLoading(true);
    setMessage(null);

    try {
      const [organizations, catalog] = await Promise.all([
        api.organizations(),
        api.organizationPermissions(),
      ]);
      const activeId = await getActiveOrganizationId();
      const nextOrganizationId = organizations.some((organization) => organization.id === activeId)
        ? activeId
        : organizations[0]?.id ?? null;

      if (nextOrganizationId) {
        await setActiveOrganizationId(nextOrganizationId);
      }

      const scoped = nextOrganizationId
        ? await loadScopedData(nextOrganizationId)
        : { members: [], roles: [], teams: [] };

      setSelectedOrganizationId(nextOrganizationId);
      setData({
        organizations,
        permissions: catalog.permissions,
        ...scoped,
      });
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to load workspace admin.');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => { void load(); }, [load]));

  async function refreshScoped(organizationId = selectedOrganizationId) {
    if (!organizationId) return;
    const scoped = await loadScopedData(organizationId);
    setData((current) => ({ ...current, ...scoped }));
  }

  async function chooseOrganization(organizationId: string) {
    setSelectedOrganizationId(organizationId);
    await setActiveOrganizationId(organizationId);
    await refreshScoped(organizationId);
  }

  async function createOrganization() {
    if (!organizationName.trim()) {
      setMessage('Organization name is required.');
      return;
    }

    setIsCreatingOrg(true);
    setMessage(null);

    try {
      const organization = await api.createOrganization({ name: organizationName.trim() });
      await setActiveOrganizationId(organization.id);
      setOrganizationName('');
      const [organizations, scoped] = await Promise.all([
        api.organizations(),
        loadScopedData(organization.id),
      ]);
      setSelectedOrganizationId(organization.id);
      setData((current) => ({ ...current, organizations, ...scoped }));
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to create organization.');
    } finally {
      setIsCreatingOrg(false);
    }
  }

  return (
    <Screen refreshing={isLoading} onRefresh={() => void load()}>
      <View style={styles.titleRow}>
        <View style={styles.flex}>
          <Text variant="title">Workspace Admin</Text>
          <Text variant="caption">Organizations, teams, roles, and owner-safe permissions.</Text>
        </View>
        {selectedOrganization ? <StatusPill label="Active org" tone="primary" /> : null}
      </View>

      {message ? (
        <Card>
          <Text variant="bodyBold" color={c.warning}>{message}</Text>
        </Card>
      ) : null}

      <SectionHeader title="Organizations" />
      <Card>
        {data.organizations.length === 0 ? (
          <Text variant="caption">Create an organization to enable team roles and org-scoped data.</Text>
        ) : (
          <View style={styles.orgList}>
            {data.organizations.map((organization) => {
              const selected = selectedOrganizationId === organization.id;
              return (
                <Pressable
                  key={organization.id}
                  accessibilityRole="button"
                  accessibilityState={{ selected }}
                  onPress={() => void chooseOrganization(organization.id)}
                  style={[
                    styles.orgCard,
                    {
                      backgroundColor: selected ? c.primaryLight : c.surfaceAlt,
                      borderColor: selected ? c.primary : c.border,
                    },
                  ]}
                >
                  <View style={styles.flex}>
                    <Text variant="bodyBold" color={selected ? c.primary : c.text}>{organization.name}</Text>
                    <Text variant="caption">{new Date(organization.createdAt).toLocaleDateString()}</Text>
                  </View>
                  {selected ? <StatusPill label="Selected" tone="primary" size="sm" /> : null}
                </Pressable>
              );
            })}
          </View>
        )}
        <Divider />
        <TextField label="New organization" value={organizationName} onChangeText={setOrganizationName} placeholder="ScoutHaul Resale Co." />
        <Button
          label={isCreatingOrg ? 'Creating...' : 'Create Organization'}
          icon="business-outline"
          disabled={isCreatingOrg}
          fullWidth
          onPress={() => void createOrganization()}
        />
      </Card>

      {selectedOrganizationId ? (
        <>
          <SectionHeader title="Roles" />
          <RoleManager
            organizationId={selectedOrganizationId}
            permissions={data.permissions}
            roles={data.roles}
            onChanged={() => refreshScoped()}
          />

          <SectionHeader title="Members" />
          <MemberManager
            organizationId={selectedOrganizationId}
            members={data.members}
            roles={data.roles}
            onChanged={() => refreshScoped()}
          />

          <SectionHeader title="Teams" />
          <TeamManager
            organizationId={selectedOrganizationId}
            teams={data.teams}
            members={data.members}
            roles={data.roles}
            onChanged={() => refreshScoped()}
          />
        </>
      ) : null}
    </Screen>
  );
}

async function loadScopedData(organizationId: string) {
  const [members, roles, teams] = await Promise.all([
    api.organizationMembers(organizationId),
    api.organizationRoles(organizationId),
    api.organizationTeams(organizationId),
  ]);

  return { members, roles, teams };
}

const styles = StyleSheet.create({
  titleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  orgList: {
    gap: spacing.sm,
  },
  orgCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    borderWidth: 1,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  flex: {
    flex: 1,
  },
});
