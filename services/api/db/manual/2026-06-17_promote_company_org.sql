-- ============================================================================
-- One-time tenancy cleanup: promote the workspace that holds the real inventory
-- into the canonical "Overhaul Trading Company" org, link it to the storefront
-- domain, drop legacy teams, and remove the empty stray orgs.
--
-- Zero data movement: the 114 inventory items (+ photos/listings/etc.) keep their
-- OrganizationId; we only rename/clean the org record. Lowest-risk migration.
--
-- ⚠️  BACK UP THE DATABASE FIRST (Railway → Postgres → Backups, or pg_dump).
-- ⚠️  Runs in a transaction. VERIFY the SELECT output before you COMMIT.
-- Run this in the Railway Postgres "Data → Query" box (or psql).
-- ============================================================================

-- 0) Pre-check — confirm which org holds the inventory before touching anything.
SELECT o."Name", o."Id",
       (SELECT COUNT(*) FROM "InventoryItems" i WHERE i."OrganizationId" = o."Id") AS items
FROM "Organizations" o
ORDER BY items DESC;
-- Expect: bs.development.contact's workspace (4bdd72a4…) = 114, others = 0.
-- If the 114-item org id differs, update the ids below before running the block.

BEGIN;

-- 1) Promote the 114-item org to the canonical company org + link the domain.
UPDATE "Organizations"
SET "Name" = 'Overhaul Trading Company',
    "StoreDomain" = 'overhaultrading.com',
    "UpdatedAt" = now()
WHERE "Id" = '4bdd72a4-99e8-4598-9499-8f08270a9095';

-- 2) Drop legacy teams in that org (team members cascade) — clean slate on teams.
DELETE FROM "OrganizationTeams"
WHERE "OrganizationId" = '4bdd72a4-99e8-4598-9499-8f08270a9095';

-- 3) Remove the empty stray orgs (0 inventory). Members/roles/teams/invites cascade.
DELETE FROM "Organizations"
WHERE "Id" IN (
  '78fa4f11-d207-45df-9ca8-b6a0bab3cae6',  -- Bsresales (empty)
  '5bf0dbea-50aa-4438-a7a0-9285abc530c7'   -- shelbya.lorente's workspace (empty)
);

-- 4) VERIFY — expect ONE row: "Overhaul Trading Company", StoreDomain set, items = 114.
SELECT o."Name", o."Id", o."StoreDomain",
       (SELECT COUNT(*) FROM "InventoryItems" i WHERE i."OrganizationId" = o."Id") AS items,
       (SELECT COUNT(*) FROM "OrganizationTeams" t WHERE t."OrganizationId" = o."Id") AS teams,
       (SELECT COUNT(*) FROM "OrganizationMembers" m WHERE m."OrganizationId" = o."Id") AS members
FROM "Organizations" o;

-- ✅ If the row is correct:   COMMIT;
-- ❌ If anything looks wrong:  ROLLBACK;
