import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { useEffect, useMemo, useState } from 'react';
import { api } from '../../api';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Divider } from '../../components/Divider';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import { radius, spacing } from '../../components/theme';
import { useColors } from '../../context/ThemeContext';
import type { OrganizationInviteCode, OrganizationRoleDefinition, OrganizationTeam } from '../../domain/types';

type InviteCodeManagerProps = {
  organizationId: string;
  roles: OrganizationRoleDefinition[];
  teams: OrganizationTeam[];
};

export function InviteCodeManager({ organizationId, roles, teams }: InviteCodeManagerProps) {
  const c = useColors();
  const assignableRoles = useMemo(() => roles.filter((role) => !role.isOwnerRole), [roles]);
  const defaultRoleId = assignableRoles[0]?.id ?? null;
  const [inviteCodes, setInviteCodes] = useState<OrganizationInviteCode[]>([]);
  const [name, setName] = useState('New teammate');
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(defaultRoleId);
  const [selectedTeamId, setSelectedTeamId] = useState<string | null>(null);
  const [maxUses, setMaxUses] = useState('1');
  const [createdCode, setCreatedCode] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    if (!selectedRoleId && defaultRoleId) {
      setSelectedRoleId(defaultRoleId);
    }
  }, [defaultRoleId, selectedRoleId]);

  useEffect(() => {
    void loadInviteCodes();
  }, [organizationId]);

  async function loadInviteCodes() {
    setIsLoading(true);
    try {
      setInviteCodes(await api.organizationInviteCodes(organizationId));
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to load invite codes.');
    } finally {
      setIsLoading(false);
    }
  }

  async function createInviteCode() {
    if (!name.trim()) {
      setMessage('Invite name is required.');
      return;
    }

    const parsedMaxUses = Number.parseInt(maxUses, 10);
    if (maxUses.trim() && (!Number.isFinite(parsedMaxUses) || parsedMaxUses <= 0)) {
      setMessage('Max uses must be a positive number.');
      return;
    }

    setIsSaving(true);
    setMessage(null);
    setCreatedCode(null);

    try {
      const result = await api.createOrganizationInviteCode(organizationId, {
        name: name.trim(),
        roleId: selectedRoleId,
        teamId: selectedTeamId,
        maxUses: maxUses.trim() ? parsedMaxUses : null,
        expiresAt: null,
      });
      setCreatedCode(result.code);
      setInviteCodes((current) => [result.invite, ...current]);
      setName('New teammate');
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to create invite code.');
    } finally {
      setIsSaving(false);
    }
  }

  async function deactivateInviteCode(inviteCode: OrganizationInviteCode) {
    setIsSaving(true);
    setMessage(null);
    try {
      const updated = await api.deactivateOrganizationInviteCode(organizationId, inviteCode.id);
      setInviteCodes((current) => current.map((item) => item.id === updated.id ? updated : item));
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to deactivate invite code.');
    } finally {
      setIsSaving(false);
    }
  }

  function confirmDeactivateInviteCode(inviteCode: OrganizationInviteCode) {
    Alert.alert('Deactivate invite code?', inviteCode.name, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Deactivate', style: 'destructive', onPress: () => void deactivateInviteCode(inviteCode) },
    ]);
  }

  return (
    <Card>
      <View style={styles.headerRow}>
        <View style={styles.flex}>
          <Text variant="subtitle">Invite Codes</Text>
          <Text variant="caption">Codes let new users join this organization without manual member setup.</Text>
        </View>
        {isLoading ? <StatusPill label="Loading" tone="neutral" size="sm" /> : null}
      </View>

      {message ? <Text variant="caption" color={c.warning}>{message}</Text> : null}

      {createdCode ? (
        <View style={[styles.createdCode, { backgroundColor: c.primaryLight, borderColor: c.primary }]}>
          <Text variant="label" color={c.primary}>New invite code</Text>
          <Text variant="subtitle" color={c.primary}>{createdCode}</Text>
          <Text variant="caption">Share this once. Only the prefix is shown later.</Text>
        </View>
      ) : null}

      <TextField label="Invite name" value={name} onChangeText={setName} placeholder="Saturday sourcing crew" />

      <Text variant="label">Role</Text>
      <RoleChips roles={assignableRoles} value={selectedRoleId} onChange={setSelectedRoleId} />

      <Text variant="label">Team assignment</Text>
      <TeamChips teams={teams} value={selectedTeamId} onChange={setSelectedTeamId} />

      <TextField label="Max uses" value={maxUses} onChangeText={setMaxUses} keyboardType="number-pad" placeholder="1" />
      <Button
        label={isSaving ? 'Creating...' : 'Create Invite Code'}
        icon="key-outline"
        disabled={isSaving}
        fullWidth
        onPress={() => void createInviteCode()}
      />

      <Divider />

      {inviteCodes.length === 0 ? (
        <Text variant="caption">No invite codes yet.</Text>
      ) : (
        inviteCodes.map((inviteCode, index) => (
          <View key={inviteCode.id}>
            {index > 0 ? <Divider /> : null}
            <View style={styles.inviteRow}>
              <View style={styles.flex}>
                <View style={styles.inline}>
                  <Text variant="bodyBold">{inviteCode.name}</Text>
                  <StatusPill label={inviteCode.isActive ? 'Active' : 'Inactive'} tone={inviteCode.isActive ? 'success' : 'neutral'} size="sm" />
                </View>
                <Text variant="caption">
                  {inviteCode.codePrefix} · {inviteCode.useCount}{inviteCode.maxUses ? `/${inviteCode.maxUses}` : ''} uses
                </Text>
                <Text variant="caption">
                  {inviteCode.roleName ?? 'Default operator'}{inviteCode.teamName ? ` · ${inviteCode.teamName}` : ''}
                </Text>
              </View>
              {inviteCode.isActive ? (
                <Button
                  label="Deactivate"
                  icon="close-circle-outline"
                  variant="secondary"
                  size="sm"
                  disabled={isSaving}
                  onPress={() => confirmDeactivateInviteCode(inviteCode)}
                />
              ) : null}
            </View>
          </View>
        ))
      )}
    </Card>
  );
}

function RoleChips({
  roles,
  value,
  onChange,
}: {
  roles: OrganizationRoleDefinition[];
  value: string | null;
  onChange: (roleId: string | null) => void;
}) {
  const c = useColors();
  return (
    <View style={styles.chips}>
      <Pressable
        accessibilityRole="button"
        accessibilityState={{ selected: value === null }}
        onPress={() => onChange(null)}
        style={[
          styles.chip,
          { backgroundColor: value === null ? c.primary : c.surfaceAlt, borderColor: value === null ? c.primary : c.border },
        ]}
      >
        <Text variant="caption" color={value === null ? c.textInverse : c.textSecondary}>Default operator</Text>
      </Pressable>
      {roles.map((role) => {
        const selected = value === role.id;
        return (
          <Pressable
            key={role.id}
            accessibilityRole="button"
            accessibilityState={{ selected }}
            onPress={() => onChange(role.id)}
            style={[
              styles.chip,
              { backgroundColor: selected ? c.primary : c.surfaceAlt, borderColor: selected ? c.primary : c.border },
            ]}
          >
            <Text variant="caption" color={selected ? c.textInverse : c.textSecondary}>{role.name}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

function TeamChips({
  teams,
  value,
  onChange,
}: {
  teams: OrganizationTeam[];
  value: string | null;
  onChange: (teamId: string | null) => void;
}) {
  const c = useColors();
  return (
    <View style={styles.chips}>
      <Pressable
        accessibilityRole="button"
        accessibilityState={{ selected: value === null }}
        onPress={() => onChange(null)}
        style={[
          styles.chip,
          { backgroundColor: value === null ? c.primary : c.surfaceAlt, borderColor: value === null ? c.primary : c.border },
        ]}
      >
        <Text variant="caption" color={value === null ? c.textInverse : c.textSecondary}>No team</Text>
      </Pressable>
      {teams.map((team) => {
        const selected = value === team.id;
        return (
          <Pressable
            key={team.id}
            accessibilityRole="button"
            accessibilityState={{ selected }}
            onPress={() => onChange(team.id)}
            style={[
              styles.chip,
              { backgroundColor: selected ? c.primary : c.surfaceAlt, borderColor: selected ? c.primary : c.border },
            ]}
          >
            <Text variant="caption" color={selected ? c.textInverse : c.textSecondary}>{team.name}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  createdCode: {
    borderWidth: 1,
    borderRadius: radius.lg,
    padding: spacing.md,
    gap: spacing.xs,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  chip: {
    borderWidth: 1,
    borderRadius: radius.full,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  inviteRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  flex: {
    flex: 1,
  },
});
