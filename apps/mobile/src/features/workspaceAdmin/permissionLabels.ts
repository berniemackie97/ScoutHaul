export const defaultOperatorPermissions = [
  'inventory.manage',
  'routes.manage',
  'budgets.manage',
  'listings.manage',
  'tasks.manage',
  'reports.view',
];

const labels: Record<string, string> = {
  'organization.manage': 'Org settings',
  'organization.owners.manage': 'Owners',
  'organization.roles.manage': 'Roles',
  'organization.teams.manage': 'Teams',
  'organization.members.manage': 'Members',
  'inventory.manage': 'Inventory',
  'routes.manage': 'Routes',
  'budgets.manage': 'Budgets',
  'listings.manage': 'Listings',
  'tasks.manage': 'Tasks',
  'reports.view': 'Reports',
};

export function permissionLabel(permission: string): string {
  return labels[permission] ?? permission;
}
