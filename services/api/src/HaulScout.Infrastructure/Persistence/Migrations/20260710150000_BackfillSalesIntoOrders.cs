using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [Migration("20260710150000_BackfillSalesIntoOrders")]
    public partial class BackfillSalesIntoOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PR #68 introduced Sales.OrderId but only marketplace-order reconciliation populated it.
            // Existing manually recorded sales still looked like naked sale rows, which left Orders/Customers
            // empty or incomplete. This data-only migration creates real Order headers and OrderLines for every
            // legacy sale that is not already attached to an Order.
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE ""__SaleOrderGroups"" AS
                SELECT
                    s.""OrganizationId"",
                    s.""Platform"" AS ""Channel"",
                    NULLIF(trim(s.""GroupOrderReference""), '') AS ""ExternalOrderId"",
                    COALESCE(s.""SaleGroupId""::text, NULLIF(trim(s.""GroupOrderReference""), ''), s.""Id""::text) AS ""GroupKey"",
                    MIN(s.""SoldAt"") AS ""OrderedAt"",
                    SUM(s.""SoldPrice"") AS ""Subtotal"",
                    SUM(s.""ShippingCharged"") AS ""ShippingTotal"",
                    SUM(s.""SalesTaxCollected"") AS ""TaxTotal"",
                    CASE
                        WHEN bool_and(s.""Status"" = 'Cancelled') THEN 'Cancelled'
                        WHEN bool_and(s.""Status"" = 'Refunded') THEN 'Refunded'
                        WHEN bool_and(s.""Status"" = 'Delivered') THEN 'Delivered'
                        WHEN bool_or(s.""Status"" IN ('Shipped', 'Delivered')) THEN 'Shipped'
                        WHEN bool_or(s.""Status"" = 'Packed') THEN 'Fulfilling'
                        ELSE 'Paid'
                    END AS ""OrderStatus"",
                    CASE
                        WHEN bool_and(s.""Status"" = 'Refunded') THEN 'Refunded'
                        WHEN bool_or(s.""Status"" = 'PartiallyRefunded') THEN 'PartiallyRefunded'
                        ELSE 'Paid'
                    END AS ""PaymentStatus"",
                    CASE
                        WHEN bool_and(s.""Status"" IN ('Shipped', 'Delivered')) THEN 'Fulfilled'
                        WHEN bool_or(s.""Status"" IN ('Packed', 'Shipped', 'Delivered')) THEN 'Partial'
                        ELSE 'Unfulfilled'
                    END AS ""FulfillmentStatus""
                FROM ""Sales"" s
                WHERE s.""OrderId"" IS NULL
                GROUP BY s.""OrganizationId"", s.""Platform"", NULLIF(trim(s.""GroupOrderReference""), ''), COALESCE(s.""SaleGroupId""::text, NULLIF(trim(s.""GroupOrderReference""), ''), s.""Id""::text);

                ALTER TABLE ""__SaleOrderGroups"" ADD COLUMN ""OrderId"" uuid;

                UPDATE ""__SaleOrderGroups"" g
                SET ""OrderId"" = o.""Id""
                FROM ""Orders"" o
                WHERE g.""ExternalOrderId"" IS NOT NULL
                  AND o.""OrganizationId"" = g.""OrganizationId""
                  AND o.""Channel"" = g.""Channel""
                  AND o.""ExternalOrderId"" = g.""ExternalOrderId"";

                UPDATE ""__SaleOrderGroups""
                SET ""OrderId"" = gen_random_uuid()
                WHERE ""OrderId"" IS NULL;

                INSERT INTO ""Orders""
                    (""Id"", ""OrganizationId"", ""OrderNumber"", ""Status"", ""PaymentStatus"", ""FulfillmentStatus"", ""Email"",
                     ""Subtotal"", ""DiscountTotal"", ""TaxTotal"", ""ShippingTotal"", ""GrandTotal"", ""Currency"",
                     ""IsLocalPickup"", ""CreatedAt"", ""UpdatedAt"", ""PaidAt"", ""Channel"", ""ExternalOrderId"")
                SELECT
                    g.""OrderId"",
                    g.""OrganizationId"",
                    left(regexp_replace(COALESCE(g.""ExternalOrderId"", g.""Channel"" || '-sale'), '[^A-Za-z0-9-]', '-', 'g'), 31)
                        || '-' || left(g.""OrderId""::text, 8),
                    g.""OrderStatus"",
                    g.""PaymentStatus"",
                    g.""FulfillmentStatus"",
                    NULL,
                    g.""Subtotal"",
                    0,
                    g.""TaxTotal"",
                    g.""ShippingTotal"",
                    g.""Subtotal"" + g.""ShippingTotal"" + g.""TaxTotal"",
                    'USD',
                    false,
                    g.""OrderedAt"",
                    now(),
                    g.""OrderedAt"",
                    g.""Channel"",
                    g.""ExternalOrderId""
                FROM ""__SaleOrderGroups"" g
                WHERE NOT EXISTS (SELECT 1 FROM ""Orders"" existing WHERE existing.""Id"" = g.""OrderId"");

                UPDATE ""Sales"" s
                SET ""OrderId"" = g.""OrderId""
                FROM ""__SaleOrderGroups"" g
                WHERE s.""OrderId"" IS NULL
                  AND s.""OrganizationId"" = g.""OrganizationId""
                  AND s.""Platform"" = g.""Channel""
                  AND COALESCE(s.""SaleGroupId""::text, NULLIF(trim(s.""GroupOrderReference""), ''), s.""Id""::text) = g.""GroupKey"";

                INSERT INTO ""OrderLines""
                    (""Id"", ""OrganizationId"", ""OrderId"", ""InventoryItemId"", ""TitleSnapshot"", ""SkuSnapshot"",
                     ""UnitPrice"", ""Quantity"", ""LineTotal"", ""UnitCostSnapshot"", ""CreatedAt"", ""UpdatedAt"")
                SELECT
                    gen_random_uuid(),
                    s.""OrganizationId"",
                    s.""OrderId"",
                    s.""InventoryItemId"",
                    COALESCE(i.""Title"", 'Inventory item'),
                    i.""Barcode"",
                    s.""SoldPrice"",
                    1,
                    s.""SoldPrice"",
                    s.""CostOfGoods"",
                    s.""SoldAt"",
                    now()
                FROM ""Sales"" s
                JOIN ""InventoryItems"" i ON i.""Id"" = s.""InventoryItemId""
                WHERE s.""OrderId"" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM ""OrderLines"" line
                      WHERE line.""OrganizationId"" = s.""OrganizationId""
                        AND line.""OrderId"" = s.""OrderId""
                        AND line.""InventoryItemId"" = s.""InventoryItemId"");

                UPDATE ""Orders"" o
                SET ""Subtotal"" = totals.""Subtotal"",
                    ""ShippingTotal"" = totals.""ShippingTotal"",
                    ""TaxTotal"" = totals.""TaxTotal"",
                    ""GrandTotal"" = totals.""Subtotal"" + totals.""ShippingTotal"" + totals.""TaxTotal"",
                    ""UpdatedAt"" = now()
                FROM (
                    SELECT ""OrderId"", SUM(""SoldPrice"") AS ""Subtotal"", SUM(""ShippingCharged"") AS ""ShippingTotal"", SUM(""SalesTaxCollected"") AS ""TaxTotal""
                    FROM ""Sales""
                    WHERE ""OrderId"" IS NOT NULL
                    GROUP BY ""OrderId""
                ) totals
                WHERE o.""Id"" = totals.""OrderId"";

                DROP TABLE ""__SaleOrderGroups"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only forward repair. Reversing would detach legitimate order history, so rollback is intentionally
            // a no-op.
        }
    }
}
