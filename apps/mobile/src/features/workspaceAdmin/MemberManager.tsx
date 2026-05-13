import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { useEffect, useMemo, useState } from 'react';
import { api } from '../../api/client';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Divider } from '../../components/Divider';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import { radius, spacing } from '../../components/theme';
import { useColors } from '../../context/ThemeContext';
import type { OrganizationMember, OrganizationRoleDefinition } from '../../domain/types';

type MemberManagerProps = {
  organizationId: string;
  members: OrganizationMember[];
  roles: OrganizationRoleDefinition[];
  onChanged: () => Promise<void>;
};

export function MemberManager({ organizationId, members, roles, onChanged }: MemberManagerProps) {
  const c = useColors();
  const defaultRoleId = useMemo(
    () => roles.find((role) => !role.isOwnerRole)?.id ?? roles[0]?.id ?? null,
    [roles],
  );
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [newMemberRoleId, setNewMemberRoleId] = useState<string | null>(defaultRoleId);
  const [memberRoleDrafts, setMemberRoleDrafts] = useState<Record<string, string | null>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    if (!newMemberRoleId && defaultRoleId) {
      setNewMemberRoleId(defaultRoleId);
    }
  }, [defaultRoleId, newMemberRoleId]);

  function roleFor(id: string | null | undefined): OrganizationRoleDefinition | undefined {
    return roles.find((role) => role.id === id);
  }

  async function addMember() {
    if (!email.trim() || !displayName.trim()) {
      setMessage('Email and display name are required.');
      return;
    }

    const selectedRole = roleFor(newMemberRoleId);
    setIsSaving(true);
    setMessage(null);

    try {
      await api.addOrganizationMember(organizationId, {
        email: email.trim(),
        displayName: displayName.trim(),
        role: selectedRole?.isOwnerRole ? 'Owner' : 'Member',
        roleId: selectedRole?.id ?? null,
      });
      setEmail('');
      setDisplayName('');
      await onChanged();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to add member.');
    } finally {
      setIsSaving(false);
    }
  }

  async function updateMemberRole(member: OrganizationMember) {
    const nextRoleId = memberRoleDrafts[member.id] ?? member.roleId ?? null;
    const selectedRole = roleFor(nextRoleId);

    setIsSaving(true);
    setMessage(null);

    try {
      await api.updateOrganizationMemberRole(organizationId, member.id, {
        roleId: selectedRole?.id ?? null,
        legacyRole: selectedRole?.isOwnerRole ? 'Owner' : 'Member',
      });
      await onChanged();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to update member role.');
    } finally {
      setIsSaving(false);
    }
  }

  async function resendInvite(member: OrganizationMember) {
    setIsSaving(true);
    setMessage(null);
    try {
      await api.resendOrganizationMemberInvite(organizationId, member.id);
      setMessage(`Invite sent to ${member.email}.`);
      await onChanged();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to resend invite.');
    } finally {
      setIsSaving(false);
    }
  }

  async function removeMember(member: OrganizationMember) {
    setIsSaving(true);
    setMessage(null);
    try {
      await api.removeOrganizationMember(organizationId, member.id);
      await onChanged();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to remove member.');
    } finally {
      setIsSaving(false);
    }
  }

  function confirmRemoveMember(member: OrganizationMember) {
    Alert.alert(
      member.inviteStatus === 'Pending' ? 'Revoke invite?' : 'Remove member?',
      member.email,
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Remove', style: 'destructive', onPress: () => void removeMember(member) },
      ],
    );
  }

  return (
    <Card>
      <Text variant="subtitle">Members</Text>
      {message ? <Text variant="caption" color={c.warning}>{message}</Text> : null}
      <TextField label="Email" value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" placeholder="teammate@example.com" />
      <TextField label="Display name" value={displayName} onChangeText={setDisplayName} placeholder="Jordan Lee" />
      <Text variant="label">Initial role</Text>
      <RoleChips roles={roles} value={newMemberRoleId} onChange={setNewMemberRoleId} />
      <Button label={isSaving ? 'Saving...' : 'Add Member'} icon="person-add-outline" disabled={isSaving} fullWidth onPress={() => void addMember()} />

      <Divider />
      {members.length === 0 ? (
        <Text variant="caption">No members yet.</Text>
      ) : (
        members.map((member, index) => {
          const currentRoleId = memberRoleDrafts[member.id] ?? member.roleId ?? null;
          const activeRole = roleFor(currentRoleId);
          const changed = currentRoleId !== (member.roleId ?? null);

          return (
            <View key={member.id}>
              {index > 0 ? <Divider /> : null}
              <View style={styles.memberHeader}>
                <View style={styles.flex}>
                  <Text variant="bodyBold">{member.displayName}</Text>
                  <Text variant="caption">{member.email}</Text>
                </View>
                <StatusPill
                  label={member.inviteStatus === 'Pending' ? 'Pending' : activeRole?.isOwnerRole ? 'Owner' : activeRole?.name ?? member.role}
                  tone={activeRole?.isOwnerRole ? 'danger' : 'neutral'}
                  size="sm"
                />
              </View>
              <RoleChips
                roles={roles}
                value={currentRoleId}
                onChange={(roleId) => setMemberRoleDrafts((current) => ({ ...current, [member.id]: roleId }))}
              />
              {changed ? (
                <Button
                  label="Save Role"
                  icon="save-outline"
                  variant="secondary"
                  size="sm"
                  disabled={isSaving}
                  onPress={() => void updateMemberRole(member)}
                />
              ) : null}
              <View style={styles.memberActions}>
                {member.inviteStatus === 'Pending' ? (
                  <Button
                    label="Resend Invite"
                    icon="mail-outline"
                    variant="secondary"
                    size="sm"
                    disabled={isSaving}
                    onPress={() => void resendInvite(member)}
                  />
                ) : null}
                <Button
                  label={member.inviteStatus === 'Pending' ? 'Revoke' : 'Remove'}
                  icon="trash-outline"
                  variant="danger"
                  size="sm"
                  disabled={isSaving}
                  onPress={() => confirmRemoveMember(member)}
                />
              </View>
            </View>
          );
        })
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
      {roles.map((role) => {
        const selected = value === role.id;
        return (
          <Pressable
            key={role.id}
            accessibilityRole="button"
            accessibilityState={{ selected }}
            onPress={() => onChange(role.id)}
            style={[
              styles.roleChip,
              {
                backgroundColor: selected ? (role.isOwnerRole ? c.danger : c.primary) : c.surfaceAlt,
                borderColor: selected ? (role.isOwnerRole ? c.danger : c.primary) : c.border,
              },
            ]}
          >
            <Text variant="caption" color={selected ? c.textInverse : c.textSecondary}>{role.name}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  memberHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  roleChip: {
    borderWidth: 1,
    borderRadius: radius.full,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  flex: {
    flex: 1,
  },
  memberActions: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
});
