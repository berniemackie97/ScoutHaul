import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { useMemo, useState } from 'react';
import { api } from '../../api';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Divider } from '../../components/Divider';
import { StatusPill } from '../../components/StatusPill';
import { Text } from '../../components/Text';
import { TextField } from '../../components/TextField';
import { radius, spacing } from '../../components/theme';
import { useColors } from '../../context/ThemeContext';
import type { OrganizationRoleDefinition } from '../../domain/types';
import { PermissionPicker } from './PermissionPicker';
import { defaultOperatorPermissions } from './permissionLabels';

type RoleManagerProps = {
  organizationId: string;
  permissions: string[];
  roles: OrganizationRoleDefinition[];
  onChanged: () => Promise<void>;
};

export function RoleManager({ organizationId, permissions, roles, onChanged }: RoleManagerProps) {
  const c = useColors();
  const safeDefaultPermissions = useMemo(
    () => defaultOperatorPermissions.filter((permission) => permissions.includes(permission)),
    [permissions],
  );
  const [editingRoleId, setEditingRoleId] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [grantsOwnerAccess, setGrantsOwnerAccess] = useState(false);
  // Null means "untouched", which reads as the defaults. Storing the defaults directly and
  // re-seeding them from an effect meant clearing every permission silently put them back,
  // and that the seed was missed entirely when `permissions` arrived after the first render.
  const [permissionDraft, setPermissionDraft] = useState<string[] | null>(null);
  const selectedPermissions = permissionDraft ?? safeDefaultPermissions;
  const [message, setMessage] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  function resetForm() {
    setEditingRoleId(null);
    setName('');
    setDescription('');
    setGrantsOwnerAccess(false);
    setPermissionDraft(null);
    setMessage(null);
  }

  function editRole(role: OrganizationRoleDefinition) {
    setEditingRoleId(role.id);
    setName(role.name);
    setDescription(role.description ?? '');
    setGrantsOwnerAccess(role.isOwnerRole);
    setPermissionDraft(role.isOwnerRole ? permissions : role.permissions);
    setMessage(null);
  }

  function togglePermission(permission: string) {
    setPermissionDraft((current) => {
      const base = current ?? safeDefaultPermissions;
      return base.includes(permission)
        ? base.filter((item) => item !== permission)
        : [...base, permission];
    });
  }

  async function submitRole() {
    if (!name.trim()) {
      setMessage('Role name is required.');
      return;
    }

    if (!grantsOwnerAccess && selectedPermissions.length === 0) {
      setMessage('Choose at least one permission.');
      return;
    }

    setIsSaving(true);
    setMessage(null);

    try {
      const body = {
        name: name.trim(),
        description: description.trim() || null,
        grantsOwnerAccess,
        permissions: grantsOwnerAccess ? permissions : selectedPermissions,
      };

      if (editingRoleId) {
        await api.updateOrganizationRole(organizationId, editingRoleId, body);
      } else {
        await api.createOrganizationRole(organizationId, body);
      }

      resetForm();
      await onChanged();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to save role.');
    } finally {
      setIsSaving(false);
    }
  }

  async function deleteRole(role: OrganizationRoleDefinition) {
    setIsSaving(true);
    setMessage(null);
    try {
      await api.deleteOrganizationRole(organizationId, role.id);
      await onChanged();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Unable to delete role.');
    } finally {
      setIsSaving(false);
    }
  }

  function confirmDeleteRole(role: OrganizationRoleDefinition) {
    Alert.alert(
      'Delete role?',
      role.name,
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Delete', style: 'destructive', onPress: () => void deleteRole(role) },
      ],
    );
  }

  return (
    <>
      <Card>
        <View style={styles.headerRow}>
          <View style={styles.flex}>
            <Text variant="subtitle">{editingRoleId ? 'Edit Role' : 'Create Role'}</Text>
            <Text variant="caption">Custom roles default to operator-level permissions. Owner access is explicit.</Text>
          </View>
          {editingRoleId ? <Button label="Cancel" variant="secondary" size="sm" onPress={resetForm} /> : null}
        </View>
        {message ? <Text variant="caption" color={c.warning}>{message}</Text> : null}
        <TextField label="Role name" value={name} onChangeText={setName} placeholder="Route Lead" />
        <TextField label="Description" value={description} onChangeText={setDescription} placeholder="Can manage sourcing routes and tasks" />
        <Pressable
          accessibilityRole="switch"
          accessibilityState={{ checked: grantsOwnerAccess }}
          onPress={() => setGrantsOwnerAccess((value) => !value)}
          style={[styles.ownerToggle, { borderColor: grantsOwnerAccess ? c.danger : c.border, backgroundColor: grantsOwnerAccess ? c.dangerLight : c.surfaceAlt }]}
        >
          <View style={styles.flex}>
            <Text variant="bodyBold" color={grantsOwnerAccess ? c.danger : c.text}>Grant owner access</Text>
            <Text variant="caption">
              Owners can manage owners, roles, teams, budget controls, and organization settings.
            </Text>
          </View>
          <StatusPill label={grantsOwnerAccess ? 'Owner' : 'Off'} tone={grantsOwnerAccess ? 'danger' : 'neutral'} size="sm" />
        </Pressable>
        <PermissionPicker
          permissions={permissions}
          selected={grantsOwnerAccess ? permissions : selectedPermissions}
          disabled={grantsOwnerAccess}
          onToggle={togglePermission}
        />
        <Button
          label={isSaving ? 'Saving...' : editingRoleId ? 'Update Role' : 'Create Role'}
          icon={editingRoleId ? 'save-outline' : 'add-circle-outline'}
          disabled={isSaving}
          fullWidth
          onPress={() => void submitRole()}
        />
      </Card>

      <Card>
        <Text variant="subtitle">Roles</Text>
        {roles.length === 0 ? (
          <Text variant="caption">No roles yet.</Text>
        ) : (
          roles.map((role, index) => (
            <View key={role.id}>
              {index > 0 ? <Divider /> : null}
              <View style={styles.roleRow}>
                <View style={styles.flex}>
                  <View style={styles.inline}>
                    <Text variant="bodyBold">{role.name}</Text>
                    {role.isSystem ? <StatusPill label="System" tone="neutral" size="sm" /> : null}
                    {role.isOwnerRole ? <StatusPill label="Owner" tone="danger" size="sm" /> : null}
                  </View>
                  {role.description ? <Text variant="caption">{role.description}</Text> : null}
                  <Text variant="caption">{role.isOwnerRole ? 'All permissions' : `${role.permissions.length} permissions`}</Text>
                </View>
                <View style={styles.roleActions}>
                  <Button
                    label={role.isSystem ? 'Locked' : 'Edit'}
                    variant="secondary"
                    size="sm"
                    disabled={role.isSystem}
                    onPress={() => editRole(role)}
                  />
                  {!role.isSystem && !role.isOwnerRole ? (
                    <Button
                      label="Delete"
                      icon="trash-outline"
                      variant="danger"
                      size="sm"
                      disabled={isSaving}
                      onPress={() => confirmDeleteRole(role)}
                    />
                  ) : null}
                </View>
              </View>
            </View>
          ))
        )}
      </Card>
    </>
  );
}

const styles = StyleSheet.create({
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  roleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  ownerToggle: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    borderWidth: 1,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  flex: {
    flex: 1,
  },
  roleActions: {
    gap: spacing.sm,
  },
});
