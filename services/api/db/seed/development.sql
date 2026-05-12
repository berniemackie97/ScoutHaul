insert into "Organizations" ("Id", "Name", "CreatedAt", "UpdatedAt")
values (
    '9d3c0c44-218b-49d9-90bf-07b748610001',
    'Default Resale Workspace',
    '2026-01-01T00:00:00+00:00',
    '2026-01-01T00:00:00+00:00'
)
on conflict ("Id") do nothing;

insert into "Users" ("Id", "Email", "DisplayName", "CreatedAt", "UpdatedAt")
values (
    '9d3c0c44-218b-49d9-90bf-07b748610002',
    'owner@scouthaul.local',
    'Default Owner',
    '2026-01-01T00:00:00+00:00',
    '2026-01-01T00:00:00+00:00'
)
on conflict ("Id") do nothing;

insert into "OrganizationMembers" ("Id", "OrganizationId", "UserId", "Role", "JoinedAt", "CreatedAt", "UpdatedAt")
values (
    '9d3c0c44-218b-49d9-90bf-07b748610003',
    '9d3c0c44-218b-49d9-90bf-07b748610001',
    '9d3c0c44-218b-49d9-90bf-07b748610002',
    'Owner',
    '2026-01-01T00:00:00+00:00',
    '2026-01-01T00:00:00+00:00',
    '2026-01-01T00:00:00+00:00'
)
on conflict ("OrganizationId", "UserId") do nothing;

insert into "Budgets" ("Id", "OrganizationId", "Name", "StartingAmount", "CurrentAmount", "StartsAt", "EndsAt", "IsActive", "CreatedAt", "UpdatedAt")
values (
    '9d3c0c44-218b-49d9-90bf-07b748610004',
    '9d3c0c44-218b-49d9-90bf-07b748610001',
    'Default Operating Budget',
    1000.00,
    1000.00,
    '2026-01-01T00:00:00+00:00',
    null,
    true,
    '2026-01-01T00:00:00+00:00',
    '2026-01-01T00:00:00+00:00'
)
on conflict ("Id") do nothing;

insert into "SourceLocations" ("Id", "OrganizationId", "Name", "LocationType", "AddressLine1", "City", "Region", "PostalCode", "Notes", "CreatedAt", "UpdatedAt")
values (
    '9d3c0c44-218b-49d9-90bf-07b748610005',
    '9d3c0c44-218b-49d9-90bf-07b748610001',
    'Default Thrift Store',
    'Thrift',
    null,
    null,
    null,
    null,
    null,
    '2026-01-01T00:00:00+00:00',
    '2026-01-01T00:00:00+00:00'
)
on conflict ("Id") do nothing;
