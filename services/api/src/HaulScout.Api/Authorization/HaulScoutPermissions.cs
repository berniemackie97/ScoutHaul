namespace HaulScout.Api.Authorization;

public static class HaulScoutPermissions
{
    public const string ManageOrganization = "organization.manage";
    public const string ManageOwners = "organization.owners.manage";
    public const string ManageRoles = "organization.roles.manage";
    public const string ManageTeams = "organization.teams.manage";
    public const string ManageMembers = "organization.members.manage";
    public const string ManageInventory = "inventory.manage";
    public const string ManageRoutes = "routes.manage";
    public const string ManageBudgets = "budgets.manage";
    public const string ManageListings = "listings.manage";
    public const string ManageTasks = "tasks.manage";
    public const string ViewReports = "reports.view";

    public static readonly IReadOnlyList<string> All =
    [
        ManageOrganization,
        ManageOwners,
        ManageRoles,
        ManageTeams,
        ManageMembers,
        ManageInventory,
        ManageRoutes,
        ManageBudgets,
        ManageListings,
        ManageTasks,
        ViewReports
    ];

    public static readonly IReadOnlyList<string> OperatorDefaults =
    [
        ManageInventory,
        ManageRoutes,
        ManageBudgets,
        ManageListings,
        ManageTasks,
        ViewReports
    ];

    public static bool IsKnown(string permission) => All.Contains(permission, StringComparer.Ordinal);
}
