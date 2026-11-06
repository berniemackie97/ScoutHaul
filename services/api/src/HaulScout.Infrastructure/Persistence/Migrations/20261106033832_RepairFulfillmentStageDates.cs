using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Two data repairs, each audited with the values it replaced.
    /// 1. "Mark delivered" used to stamp shipped, delivered and (when unrecorded) packed with the same instant, the
    ///    moment of the click. Those orders are found by that signature in their own audit trail; shipped goes back to
    ///    what the audit says it was (or the marketplace's date, or nothing), a stamped packed is cleared, and delivered
    ///    takes the marketplace's date when it has an earlier one.
    /// 2. A scan whose only news was "delivered" never reached the order. Delivery the marketplace already reported
    ///    is copied onto orders still waiting for it.
    /// </summary>
    public partial class RepairFulfillmentStageDates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE stage_repair AS
                WITH stamped AS (
                    SELECT DISTINCT ON (a."EntityId")
                        a."EntityId" AS order_id,
                        NULLIF(a."DetailsJson"->'Before'->>'ShippedAt', '')::timestamptz AS shipped_before,
                        (a."DetailsJson"->'After'->>'ShippedAt')::timestamptz AS stamped_at
                    FROM "AuditLogs" a
                    WHERE a."EntityName" = 'Order'
                      AND a."Action" = 'OrderTrackingUpdated'
                      AND jsonb_typeof(a."DetailsJson"->'After'->'ShippedAt') = 'string'
                      AND a."DetailsJson"->'After'->>'ShippedAt' = a."DetailsJson"->'After'->>'DeliveredAt'
                      AND jsonb_typeof(a."DetailsJson"->'Before'->'DeliveredAt') IS DISTINCT FROM 'string'
                      AND a."DetailsJson"->'Before'->>'ShippedAt' IS DISTINCT FROM a."DetailsJson"->'After'->>'ShippedAt'
                      -- The click's clock has sub-second precision; a date typed into the form never does.
                      AND (a."DetailsJson"->'After'->>'ShippedAt')::timestamptz
                          <> date_trunc('second', (a."DetailsJson"->'After'->>'ShippedAt')::timestamptz)
                    ORDER BY a."EntityId", a."CreatedAt" DESC
                ),
                candidates AS (
                    SELECT
                        o."Id" AS order_id,
                        o."OrganizationId" AS organization_id,
                        o."OrderNumber" AS order_number,
                        o."PackedAt" AS packed_was,
                        o."ShippedAt" AS shipped_was,
                        o."DeliveredAt" AS delivered_was,
                        s.stamped_at,
                        COALESCE(s.shipped_before, CASE WHEN p."ShippedAt" <= s.stamped_at THEN p."ShippedAt" END) AS shipped_now,
                        p."DeliveredAt" AS marketplace_delivered
                    FROM stamped s
                    JOIN "Orders" o ON o."Id" = s.order_id
                    LEFT JOIN LATERAL (
                        SELECT po."ShippedAt", po."DeliveredAt"
                        FROM "PlatformOrders" po
                        WHERE po."OrganizationId" = o."OrganizationId"
                          AND po."Platform" = o."Channel"
                          AND po."ExternalOrderId" = o."ExternalOrderId"
                        LIMIT 1
                    ) p ON TRUE
                    -- Untouched since the click: anything edited afterwards is someone's deliberate correction.
                    WHERE abs(extract(epoch FROM o."ShippedAt" - s.stamped_at)) < 0.001
                      AND abs(extract(epoch FROM o."DeliveredAt" - s.stamped_at)) < 0.001
                )
                SELECT
                    c.*,
                    CASE WHEN abs(extract(epoch FROM c.packed_was - c.stamped_at)) < 0.001 THEN NULL ELSE c.packed_was END AS packed_now,
                    CASE
                        WHEN c.marketplace_delivered IS NOT NULL
                             AND c.marketplace_delivered < c.stamped_at
                             AND (c.shipped_now IS NULL OR c.marketplace_delivered >= c.shipped_now)
                        THEN c.marketplace_delivered
                        ELSE c.delivered_was
                    END AS delivered_now
                FROM candidates c;

                UPDATE "Orders" o
                SET "PackedAt" = r.packed_now,
                    "ShippedAt" = r.shipped_now,
                    "DeliveredAt" = r.delivered_now,
                    "UpdatedAt" = now()
                FROM stage_repair r
                WHERE o."Id" = r.order_id;

                INSERT INTO "AuditLogs" ("Id", "CreatedAt", "UpdatedAt", "OrganizationId", "ActorUserId", "EntityName", "EntityId", "Action", "DetailsJson")
                SELECT gen_random_uuid(), now(), now(), r.organization_id, NULL, 'Order', r.order_id, 'OrderStageDatesRepaired',
                    jsonb_build_object(
                        'OrderNumber', r.order_number,
                        'Reason', 'Mark delivered had stamped every stage with the moment of the click.',
                        'Before', jsonb_build_object('PackedAt', r.packed_was, 'ShippedAt', r.shipped_was, 'DeliveredAt', r.delivered_was),
                        'After', jsonb_build_object('PackedAt', r.packed_now, 'ShippedAt', r.shipped_now, 'DeliveredAt', r.delivered_now))
                FROM stage_repair r;

                CREATE TEMP TABLE delivery_backfill AS
                SELECT
                    o."Id" AS order_id,
                    o."OrganizationId" AS organization_id,
                    o."OrderNumber" AS order_number,
                    o."Status" AS status_was,
                    o."ShippedAt" AS shipped_was,
                    COALESCE(o."ShippedAt", p."ShippedAt") AS shipped_now,
                    GREATEST(p."DeliveredAt", COALESCE(o."ShippedAt", p."ShippedAt", p."DeliveredAt")) AS delivered_now
                FROM "Orders" o
                JOIN LATERAL (
                    SELECT po."ShippedAt", po."DeliveredAt"
                    FROM "PlatformOrders" po
                    WHERE po."OrganizationId" = o."OrganizationId"
                      AND po."Platform" = o."Channel"
                      AND po."ExternalOrderId" = o."ExternalOrderId"
                    LIMIT 1
                ) p ON TRUE
                WHERE o."DeliveredAt" IS NULL
                  AND p."DeliveredAt" IS NOT NULL
                  AND o."Status" NOT IN ('Cancelled', 'Refunded');

                UPDATE "Orders" o
                SET "ShippedAt" = b.shipped_now,
                    "DeliveredAt" = b.delivered_now,
                    "Status" = 'Delivered',
                    "FulfillmentStatus" = 'Fulfilled',
                    "UpdatedAt" = now()
                FROM delivery_backfill b
                WHERE o."Id" = b.order_id;

                INSERT INTO "AuditLogs" ("Id", "CreatedAt", "UpdatedAt", "OrganizationId", "ActorUserId", "EntityName", "EntityId", "Action", "DetailsJson")
                SELECT gen_random_uuid(), now(), now(), b.organization_id, NULL, 'Order', b.order_id, 'OrderDeliveryFromMarketplace',
                    jsonb_build_object(
                        'OrderNumber', b.order_number,
                        'Reason', 'The marketplace had reported delivery; the order had not been told.',
                        'Before', jsonb_build_object('Status', b.status_was, 'ShippedAt', b.shipped_was, 'DeliveredAt', NULL),
                        'After', jsonb_build_object('Status', 'Delivered', 'ShippedAt', b.shipped_now, 'DeliveredAt', b.delivered_now))
                FROM delivery_backfill b;

                DROP TABLE stage_repair;
                DROP TABLE delivery_backfill;
                """);
        }

        // Each repaired order carries an audit entry with the values it replaced.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
