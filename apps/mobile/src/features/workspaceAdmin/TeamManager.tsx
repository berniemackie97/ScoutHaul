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
import type { OrganizationMember, OrganizationRoleDefinition, OrganizationTeam, OrganizationTeamMember } from '../../domain/types';

type TeamManagerProps = {
  organizationId: string;
  teams: OrganizationTeam[];
  members: OrganizationMember[];
  roles: OrganizationRoleDefinition[];
  onChanged: () => Promise<void>;
};

export function TeamManager({ organizationId, teams, members, roles, onChanged }: TeamManagerProps) {
  const c = useColors();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [selectedTeamId, setSelectedTeamId] = useState<string | null>(teams[0]?.id ?? null);
  const [selectedUserId, setSelectedUserId] = useState<string | null>(members[0]?.userId ?? null);
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(roles.find((role) => !role.isOwnerRole)?.id ?? roles[0]?.id ?? null);
  const [teamMembers, setTeamMembers] = useState<OrganizationTeamMember[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const selectedTeam = useMemo(
    () => teams.find((team) => team.id === selectedTeamId),
    [selectedTeamId, teams],
  );

  useEffect(() => {
    if (!selectedTeamId && teams[0]) {
      setSelectedTeamId(teams[0].id);
    }
  }, [selectedTeamId, teams]);

  useEffect(() => {
    if (!selectedUserId && members[0]) {
      setSelectedUserId(members[0].userId);
    }
  }, [members, selectedUserId]);

  useEffect(() => {
    if (!selectedRoleId && roles[0]) {
      setSelectedRoleId(roles.find((role) => !role.isOwnerRole)?.id ?? roles[0].id);
    }
  }, [roles, selectedRoleId]);

  useEffect(() => {
    let isMounted = true;
    void (async () => {
      if (!selectedTeamId) {
        setTeamMembers([]);
        return;
      }

      try {
        const result = await api.organizationTeamMembers(organizationId, selectedTeamId);
        if (isMounted) setTeamMembers(result);
      } catch (error) {
        if (isMounted) setMessage(error instanceof Error ? error.message : 'Unable to load team members.');
      }
    })();

    return () => {
      isMounted = false;
    };
  }, [organizationId, selectedTeamId]);

  async function createTeam() {
    if (!name.trim()) {
      setMessage('Team name is required.');
      return;
    }

    setIsSaving(true);
    setMessage(null);

    try {
      const team = await api.createOrganizationTeam(organizationId, {
        name: name.trim(),
        description: description.trim() || null,
      });
      setName('');
      setDescription('');
      setSelectedTeamId(team.id);
      await onChanged();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to create team.');
    } finally {
      setIsSaving(false);
    }
  }

  async function addTeamMember() {
    if (!selectedTeamId || !selectedUserId) {
      setMessage('Choose a team and organization member.');
      return;
    }

    setIsSaving(true);
    setMessage(null);

    try {
      await api.addOrganizationTeamMember(organizationId, selectedTeamId, {
        userId: selectedUserId,
        roleId: selectedRoleId,
      });
      const result = await api.organizationTeamMembers(organizationId, selectedTeamId);
      setTeamMembers(result);
      await onChanged();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to add team member.');
    } finally {
      setIsSaving(false);
    }
  }

  async function deleteTeam(team: OrganizationTeam) {
    setIsSaving(true);
    setMessage(null);
    try {
      await api.deleteOrganizationTeam(organizationId, team.id);
      setSelectedTeamId(null);
      setTeamMembers([]);
      await onChanged();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to delete team.');
    } finally {
      setIsSaving(false);
    }
  }

  async function removeTeamMember(member: OrganizationTeamMember) {
    if (!selectedTeamId) return;
    setIsSaving(true);
    setMessage(null);
    try {
      await api.removeOrganizationTeamMember(organizationId, selectedTeamId, member.id);
      setTeamMembers((current) => current.filter((item) => item.id !== member.id));
      await onChanged();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to remove team member.');
    } finally {
      setIsSaving(false);
    }
  }

  function confirmDeleteTeam(team: OrganizationTeam) {
    Alert.alert('Delete team?', team.name, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Delete', style: 'destructive', onPress: () => void deleteTeam(team) },
    ]);
  }

  return (
    <>
      <Card>
        <Text variant="subtitle">Teams</Text>
        {message ? <Text variant="caption" color={c.warning}>{message}</Text> : null}
        <TextField label="Team name" value={name} onChangeText={setName} placeholder="Saturday Sourcing" />
        <TextField label="Description" value={description} onChangeText={setDescription} placeholder="Garage sales and estate sale runs" />
        <Button label={isSaving ? 'Saving...' : 'Create Team'} icon="people-outline" disabled={isSaving} fullWidth onPress={() => void createTeam()} />

        <Divider />
        {teams.length === 0 ? (
          <Text variant="caption">No teams yet.</Text>
        ) : (
          <View style={styles.teamGrid}>
            {teams.map((team) => {
              const selected = selectedTeamId === team.id;
              return (
                <Pressable
                  key={team.id}
                  accessibilityRole="button"
                  accessibilityState={{ selected }}
                  onPress={() => setSelectedTeamId(team.id)}
                  style={[
                    styles.teamCard,
                    {
                      backgroundColor: selected ? c.primaryLight : c.surfaceAlt,
                      borderColor: selected ? c.primary : c.border,
                    },
                  ]}
                >
                  <Text variant="bodyBold" color={selected ? c.primary : c.text}>{team.name}</Text>
                  <Text variant="caption">{team.memberCount} members</Text>
                </Pressable>
              );
            })}
          </View>
        )}
      </Card>

      {selectedTeam ? (
        <Card>
          <View style={styles.headerRow}>
            <View style={styles.flex}>
              <Text variant="subtitle">{selectedTeam.name}</Text>
              <Text variant="caption">{selectedTeam.description ?? 'Team member assignments'}</Text>
            </View>
            <StatusPill label={`${teamMembers.length} assigned`} tone="primary" size="sm" />
          </View>
          <Button label="Delete Team" icon="trash-outline" variant="danger" size="sm" disabled={isSaving} onPress={() => confirmDeleteTeam(selectedTeam)} />

          {members.length === 0 ? (
            <Text variant="caption">Add organization members before assigning team members.</Text>
          ) : (
            <>
              <Text variant="label">Member</Text>
              <MemberChips members={members} value={selectedUserId} onChange={setSelectedUserId} />
              <Text variant="label">Team role</Text>
              <RoleChips roles={roles} value={selectedRoleId} onChange={setSelectedRoleId} />
              <Button label="Add To Team" icon="person-add-outline" variant="secondary" disabled={isSaving} onPress={() => void addTeamMember()} />
            </>
          )}

          <Divider />
          {teamMembers.length === 0 ? (
            <Text variant="caption">No team members assigned.</Text>
          ) : (
            teamMembers.map((member, index) => (
              <View key={member.id}>
                {index > 0 ? <Divider /> : null}
                <View style={styles.headerRow}>
                  <View style={styles.flex}>
                    <Text variant="bodyBold">{member.displayName}</Text>
                    <Text variant="caption">{member.email}</Text>
                  </View>
                  <StatusPill label={member.roleName ?? 'Member'} tone="neutral" size="sm" />
                  <Button label="Remove" icon="close-outline" variant="secondary" size="sm" disabled={isSaving} onPress={() => void removeTeamMember(member)} />
                </View>
              </View>
            ))
          )}
        </Card>
      ) : null}
    </>
  );
}

function MemberChips({
  members,
  value,
  onChange,
}: {
  members: OrganizationMember[];
  value: string | null;
  onChange: (userId: string) => void;
}) {
  const c = useColors();
  return (
    <View style={styles.chips}>
      {members.map((member) => {
        const selected = value === member.userId;
        return (
          <Pressable
            key={member.id}
            accessibilityRole="button"
            accessibilityState={{ selected }}
            onPress={() => onChange(member.userId)}
            style={[
              styles.chip,
              { backgroundColor: selected ? c.primary : c.surfaceAlt, borderColor: selected ? c.primary : c.border },
            ]}
          >
            <Text variant="caption" color={selected ? c.textInverse : c.textSecondary}>{member.displayName}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

function RoleChips({
  roles,
  value,
  onChange,
}: {
  roles: OrganizationRoleDefinition[];
  value: string | null;
  onChange: (roleId: string) => void;
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

const styles = StyleSheet.create({
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  teamGrid: {
    gap: spacing.sm,
  },
  teamCard: {
    borderWidth: 1,
    borderRadius: radius.md,
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
  flex: {
    flex: 1,
  },
});
