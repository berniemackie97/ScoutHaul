// The API's permission keys (HaulScoutPermissions), and what each is called where a person reads it.
export const permission = {
  organization: "organization.manage",
  owners: "organization.owners.manage",
  roles: "organization.roles.manage",
  teams: "organization.teams.manage",
  members: "organization.members.manage",
  inventory: "inventory.manage",
  routes: "routes.manage",
  budgets: "budgets.manage",
  finance: "finance.manage",
  listings: "listings.manage",
  orders: "orders.manage",
  tasks: "tasks.manage",
  reports: "reports.view",
} as const;

export type Permission = (typeof permission)[keyof typeof permission];

const labels: Record<string, string> = {
  [permission.organization]: "Store and organization settings",
  [permission.owners]: "Owners",
  [permission.roles]: "Roles",
  [permission.teams]: "Teams",
  [permission.members]: "Staff and invites",
  [permission.inventory]: "Inventory",
  [permission.routes]: "Sourcing routes",
  [permission.budgets]: "Budgets",
  [permission.finance]: "Purchases and money",
  [permission.listings]: "Listings and channels",
  [permission.orders]: "Orders and customers",
  [permission.tasks]: "Tasks",
  [permission.reports]: "Reports",
};

export function permissionLabel(key: string): string {
  return labels[key] ?? key;
}

/** Every one of these, or any one of them. */
export type Requirement = { all: readonly Permission[] } | { any: readonly Permission[] };

/** What the signed-in person may do. Null when the session could not be read: then nothing is hidden, and each page says what went wrong. */
export type Granted = ReadonlySet<string> | null;

export function allows(granted: Granted, requirement: Requirement | undefined): boolean {
  if (!requirement || granted === null) return true;
  return "all" in requirement
    ? requirement.all.every((key) => granted.has(key))
    : requirement.any.some((key) => granted.has(key));
}

/** The permissions a requirement names that this person does not hold, for saying what to ask for. */
export function missing(granted: Granted, requirement: Requirement): Permission[] {
  if (granted === null) return [];
  const keys = "all" in requirement ? requirement.all : requirement.any;
  return keys.filter((key) => !granted.has(key));
}
