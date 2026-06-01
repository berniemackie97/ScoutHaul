CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "AuditLogs" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "ActorUserId" uuid,
        "EntityName" character varying(160) NOT NULL,
        "EntityId" uuid,
        "Action" character varying(80) NOT NULL,
        "DetailsJson" jsonb,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "Expenses" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "InventoryItemId" uuid,
        "SourcingTripId" uuid,
        "Category" character varying(40) NOT NULL,
        "Description" character varying(300) NOT NULL,
        "Amount" numeric(12,2) NOT NULL,
        "OccurredAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Expenses" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "MarketAnalyses" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "ProductIdentityId" uuid,
        "InventoryItemId" uuid,
        "EstimatedSalePrice" numeric(12,2),
        "PricingBasis" character varying(40) NOT NULL,
        "ConfidenceScore" integer NOT NULL,
        "SoldCompCount" integer NOT NULL,
        "ActiveListingCount" integer NOT NULL,
        "WarningsJson" jsonb NOT NULL,
        "ReasonsJson" jsonb NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MarketAnalyses" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "Organizations" (
        "Id" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Organizations" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "Users" (
        "Id" uuid NOT NULL,
        "Email" character varying(320) NOT NULL,
        "DisplayName" character varying(160) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "BuyRecommendations" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "MarketAnalysisId" uuid,
        "ProductIdentityId" uuid,
        "InventoryItemId" uuid,
        "PricingBasis" character varying(40) NOT NULL,
        "Decision" character varying(40) NOT NULL,
        "RecommendedMaxBuyPrice" numeric(12,2),
        "ExpectedNetProfit" numeric(12,2),
        "RoiPercent" numeric(9,2),
        "MarginPercent" numeric(9,2),
        "BreakEvenPrice" numeric(12,2),
        "WarningsJson" jsonb NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_BuyRecommendations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_BuyRecommendations_MarketAnalyses_MarketAnalysisId" FOREIGN KEY ("MarketAnalysisId") REFERENCES "MarketAnalyses" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "Budgets" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "StartingAmount" numeric(12,2) NOT NULL,
        "CurrentAmount" numeric(12,2) NOT NULL,
        "StartsAt" timestamp with time zone NOT NULL,
        "EndsAt" timestamp with time zone,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Budgets" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Budgets_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "ProductIdentities" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "Title" character varying(300) NOT NULL,
        "Brand" character varying(160),
        "Model" character varying(160),
        "Category" character varying(160),
        "Upc" character varying(32),
        "Ean" character varying(32),
        "Isbn" character varying(32),
        "Sku" character varying(80),
        "SearchQuery" character varying(500),
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ProductIdentities" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ProductIdentities_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "SourceLocations" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "LocationType" character varying(80),
        "AddressLine1" character varying(240),
        "City" character varying(120),
        "Region" character varying(120),
        "PostalCode" character varying(32),
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_SourceLocations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SourceLocations_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "SourcingTrips" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "StartedAt" timestamp with time zone NOT NULL,
        "EndedAt" timestamp with time zone,
        "Notes" text,
        "CashBudget" numeric(12,2),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_SourcingTrips" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SourcingTrips_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "OrganizationMembers" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Role" character varying(32) NOT NULL,
        "JoinedAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OrganizationMembers" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OrganizationMembers_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_OrganizationMembers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "InventoryItems" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "ProductIdentityId" uuid,
        "SourceLocationId" uuid,
        "SourcingTripId" uuid,
        "Title" character varying(300) NOT NULL,
        "Description" text,
        "Condition" character varying(120),
        "Status" character varying(40) NOT NULL,
        "SellerAskingPrice" numeric(12,2),
        "PurchasePrice" numeric(12,2),
        "EstimatedSalePrice" numeric(12,2),
        "Notes" text,
        "Barcode" character varying(80),
        "PhotoLocalReferencesJson" text,
        "IsArchived" boolean NOT NULL,
        "PurchasedAt" timestamp with time zone,
        "ListedAt" timestamp with time zone,
        "SoldAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_InventoryItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_InventoryItems_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_InventoryItems_ProductIdentities_ProductIdentityId" FOREIGN KEY ("ProductIdentityId") REFERENCES "ProductIdentities" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_InventoryItems_SourceLocations_SourceLocationId" FOREIGN KEY ("SourceLocationId") REFERENCES "SourceLocations" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_InventoryItems_SourcingTrips_SourcingTripId" FOREIGN KEY ("SourcingTripId") REFERENCES "SourcingTrips" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "Listings" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "InventoryItemId" uuid NOT NULL,
        "Platform" character varying(80) NOT NULL,
        "ListingUrl" character varying(1500),
        "ListPrice" numeric(12,2) NOT NULL,
        "Status" character varying(32) NOT NULL,
        "ListedAt" timestamp with time zone,
        "EndedAt" timestamp with time zone,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Listings" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Listings_InventoryItems_InventoryItemId" FOREIGN KEY ("InventoryItemId") REFERENCES "InventoryItems" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "MarketObservations" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "ProductIdentityId" uuid,
        "InventoryItemId" uuid,
        "ObservationType" character varying(40) NOT NULL,
        "Source" character varying(80) NOT NULL,
        "SourceUrl" character varying(1500),
        "Title" character varying(300) NOT NULL,
        "Price" numeric(12,2) NOT NULL,
        "ShippingPrice" numeric(12,2),
        "TotalPrice" numeric(12,2) NOT NULL,
        "Currency" character varying(8),
        "Condition" character varying(120),
        "ObservedAt" timestamp with time zone NOT NULL,
        "SoldAt" timestamp with time zone,
        "Notes" text,
        "IsArchived" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MarketObservations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MarketObservations_InventoryItems_InventoryItemId" FOREIGN KEY ("InventoryItemId") REFERENCES "InventoryItems" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_MarketObservations_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_MarketObservations_ProductIdentities_ProductIdentityId" FOREIGN KEY ("ProductIdentityId") REFERENCES "ProductIdentities" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "Tasks" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "AssignedToUserId" uuid,
        "InventoryItemId" uuid,
        "Title" character varying(240) NOT NULL,
        "Description" text,
        "Status" character varying(32) NOT NULL,
        "DueAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Tasks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Tasks_InventoryItems_InventoryItemId" FOREIGN KEY ("InventoryItemId") REFERENCES "InventoryItems" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_Tasks_Users_AssignedToUserId" FOREIGN KEY ("AssignedToUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "Sales" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "InventoryItemId" uuid NOT NULL,
        "ListingId" uuid,
        "Platform" character varying(80) NOT NULL,
        "SoldPrice" numeric(12,2) NOT NULL,
        "PlatformFees" numeric(12,2) NOT NULL,
        "PaymentFees" numeric(12,2) NOT NULL,
        "ShippingCost" numeric(12,2) NOT NULL,
        "SuppliesCost" numeric(12,2) NOT NULL,
        "OtherCosts" numeric(12,2) NOT NULL,
        "NetProfit" numeric(12,2) NOT NULL,
        "Status" character varying(32) NOT NULL,
        "SoldAt" timestamp with time zone NOT NULL,
        "Notes" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Sales" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Sales_InventoryItems_InventoryItemId" FOREIGN KEY ("InventoryItemId") REFERENCES "InventoryItems" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_Sales_Listings_ListingId" FOREIGN KEY ("ListingId") REFERENCES "Listings" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE TABLE "Shipments" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "SaleId" uuid NOT NULL,
        "Carrier" character varying(80),
        "TrackingNumber" character varying(160),
        "Cost" numeric(12,2),
        "PackedAt" timestamp with time zone,
        "ShippedAt" timestamp with time zone,
        "DeliveredAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Shipments" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Shipments_Sales_SaleId" FOREIGN KEY ("SaleId") REFERENCES "Sales" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_AuditLogs_OrganizationId_CreatedAt" ON "AuditLogs" ("OrganizationId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Budgets_OrganizationId_IsActive" ON "Budgets" ("OrganizationId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_BuyRecommendations_MarketAnalysisId" ON "BuyRecommendations" ("MarketAnalysisId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Expenses_OrganizationId_OccurredAt" ON "Expenses" ("OrganizationId", "OccurredAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_InventoryItems_OrganizationId_IsArchived" ON "InventoryItems" ("OrganizationId", "IsArchived");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_InventoryItems_OrganizationId_Status" ON "InventoryItems" ("OrganizationId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_InventoryItems_ProductIdentityId" ON "InventoryItems" ("ProductIdentityId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_InventoryItems_SourceLocationId" ON "InventoryItems" ("SourceLocationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_InventoryItems_SourcingTripId" ON "InventoryItems" ("SourcingTripId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Listings_InventoryItemId" ON "Listings" ("InventoryItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Listings_OrganizationId_Status" ON "Listings" ("OrganizationId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_MarketAnalyses_OrganizationId_CreatedAt" ON "MarketAnalyses" ("OrganizationId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_MarketObservations_InventoryItemId_ObservationType" ON "MarketObservations" ("InventoryItemId", "ObservationType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_MarketObservations_OrganizationId_ObservationType" ON "MarketObservations" ("OrganizationId", "ObservationType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_MarketObservations_ProductIdentityId_ObservationType" ON "MarketObservations" ("ProductIdentityId", "ObservationType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_OrganizationMembers_OrganizationId_UserId" ON "OrganizationMembers" ("OrganizationId", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_OrganizationMembers_UserId" ON "OrganizationMembers" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Organizations_Name" ON "Organizations" ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_ProductIdentities_OrganizationId_Title" ON "ProductIdentities" ("OrganizationId", "Title");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_ProductIdentities_OrganizationId_Upc" ON "ProductIdentities" ("OrganizationId", "Upc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Sales_InventoryItemId" ON "Sales" ("InventoryItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Sales_ListingId" ON "Sales" ("ListingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Sales_OrganizationId_SoldAt" ON "Sales" ("OrganizationId", "SoldAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Shipments_SaleId" ON "Shipments" ("SaleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_SourceLocations_OrganizationId" ON "SourceLocations" ("OrganizationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_SourcingTrips_OrganizationId_StartedAt" ON "SourcingTrips" ("OrganizationId", "StartedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Tasks_AssignedToUserId" ON "Tasks" ("AssignedToUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Tasks_InventoryItemId" ON "Tasks" ("InventoryItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE INDEX "IX_Tasks_OrganizationId_Status" ON "Tasks" ("OrganizationId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512170917_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260512170917_InitialCreate', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "SourcingTrips" ADD "AssignedToUserId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "SourcingTrips" ADD "PlannedEndAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "SourcingTrips" ADD "PlannedStartAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "SourcingTrips" ADD "RouteDate" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "SourcingTrips" ADD "RouteType" character varying(40) NOT NULL DEFAULT 'General';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "SourcingTrips" ADD "Status" character varying(40) NOT NULL DEFAULT 'Draft';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "InventoryItems" ADD "SourcingTripStopId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "Expenses" ADD "SourcingTripStopId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE TABLE "SourcingTripStops" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "SourcingTripId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "SourceType" character varying(40) NOT NULL,
        "Address" character varying(500) NOT NULL,
        "Phone" character varying(80),
        "Url" character varying(1500),
        "Notes" text,
        "PlannedOrder" integer NOT NULL,
        "PlannedArrivalAt" timestamp with time zone,
        "EstimatedDurationMinutes" integer NOT NULL,
        "Priority" character varying(24) NOT NULL,
        "IsTimeSensitive" boolean NOT NULL,
        "TimeWindowStartAt" timestamp with time zone,
        "TimeWindowEndAt" timestamp with time zone,
        "CanBeSkipped" boolean NOT NULL,
        "BudgetCap" numeric(12,2),
        "Status" character varying(32) NOT NULL,
        "ActualArrivalAt" timestamp with time zone,
        "ActualDepartureAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_SourcingTripStops" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SourcingTripStops_SourcingTrips_SourcingTripId" FOREIGN KEY ("SourcingTripId") REFERENCES "SourcingTrips" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE TABLE "SourcingStopChecklistItems" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "SourcingTripStopId" uuid NOT NULL,
        "Text" character varying(240) NOT NULL,
        "SortOrder" integer NOT NULL,
        "IsRequired" boolean NOT NULL,
        "IsCompleted" boolean NOT NULL,
        "CompletedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_SourcingStopChecklistItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SourcingStopChecklistItems_SourcingTripStops_SourcingTripSt~" FOREIGN KEY ("SourcingTripStopId") REFERENCES "SourcingTripStops" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE INDEX "IX_SourcingTrips_AssignedToUserId" ON "SourcingTrips" ("AssignedToUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE INDEX "IX_SourcingTrips_OrganizationId_RouteDate" ON "SourcingTrips" ("OrganizationId", "RouteDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE INDEX "IX_SourcingTrips_OrganizationId_Status" ON "SourcingTrips" ("OrganizationId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE INDEX "IX_InventoryItems_SourcingTripStopId" ON "InventoryItems" ("SourcingTripStopId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE INDEX "IX_Expenses_SourcingTripStopId" ON "Expenses" ("SourcingTripStopId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE INDEX "IX_SourcingStopChecklistItems_OrganizationId_SourcingTripStopI~" ON "SourcingStopChecklistItems" ("OrganizationId", "SourcingTripStopId", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE INDEX "IX_SourcingStopChecklistItems_SourcingTripStopId" ON "SourcingStopChecklistItems" ("SourcingTripStopId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE INDEX "IX_SourcingTripStops_OrganizationId_SourcingTripId_PlannedOrder" ON "SourcingTripStops" ("OrganizationId", "SourcingTripId", "PlannedOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE INDEX "IX_SourcingTripStops_OrganizationId_Status" ON "SourcingTripStops" ("OrganizationId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    CREATE INDEX "IX_SourcingTripStops_SourcingTripId" ON "SourcingTripStops" ("SourcingTripId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "Expenses" ADD CONSTRAINT "FK_Expenses_SourcingTripStops_SourcingTripStopId" FOREIGN KEY ("SourcingTripStopId") REFERENCES "SourcingTripStops" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "InventoryItems" ADD CONSTRAINT "FK_InventoryItems_SourcingTripStops_SourcingTripStopId" FOREIGN KEY ("SourcingTripStopId") REFERENCES "SourcingTripStops" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    ALTER TABLE "SourcingTrips" ADD CONSTRAINT "FK_SourcingTrips_Users_AssignedToUserId" FOREIGN KEY ("AssignedToUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260512233856_SourcingRoutes') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260512233856_SourcingRoutes', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513010609_ExternalAuthSubjects') THEN
    ALTER TABLE "Users" ADD "ExternalSubject" character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513010609_ExternalAuthSubjects') THEN
    CREATE UNIQUE INDEX "IX_Users_ExternalSubject" ON "Users" ("ExternalSubject");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513010609_ExternalAuthSubjects') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260513010609_ExternalAuthSubjects', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513012245_ExternalAuthAccounts') THEN
    CREATE TABLE "ExternalAuthAccounts" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Provider" character varying(80) NOT NULL,
        "ProviderAccountId" character varying(256) NOT NULL,
        "Email" character varying(320),
        "DisplayName" character varying(160),
        "LinkedAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ExternalAuthAccounts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ExternalAuthAccounts_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513012245_ExternalAuthAccounts') THEN
    CREATE UNIQUE INDEX "IX_ExternalAuthAccounts_Provider_ProviderAccountId" ON "ExternalAuthAccounts" ("Provider", "ProviderAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513012245_ExternalAuthAccounts') THEN
    CREATE INDEX "IX_ExternalAuthAccounts_UserId" ON "ExternalAuthAccounts" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513012245_ExternalAuthAccounts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260513012245_ExternalAuthAccounts', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    ALTER TABLE "OrganizationMembers" ADD "RoleId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE TABLE "OrganizationRoles" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "Name" character varying(120) NOT NULL,
        "Description" character varying(500),
        "IsSystem" boolean NOT NULL,
        "IsOwnerRole" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OrganizationRoles" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OrganizationRoles_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE TABLE "OrganizationTeams" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "Name" character varying(120) NOT NULL,
        "Description" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OrganizationTeams" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OrganizationTeams_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE TABLE "OrganizationRolePermissions" (
        "Id" uuid NOT NULL,
        "RoleId" uuid NOT NULL,
        "Permission" character varying(120) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OrganizationRolePermissions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OrganizationRolePermissions_OrganizationRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "OrganizationRoles" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE TABLE "OrganizationTeamMembers" (
        "Id" uuid NOT NULL,
        "TeamId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "RoleId" uuid,
        "JoinedAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OrganizationTeamMembers" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OrganizationTeamMembers_OrganizationRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "OrganizationRoles" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_OrganizationTeamMembers_OrganizationTeams_TeamId" FOREIGN KEY ("TeamId") REFERENCES "OrganizationTeams" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_OrganizationTeamMembers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE INDEX "IX_OrganizationMembers_RoleId" ON "OrganizationMembers" ("RoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE UNIQUE INDEX "IX_OrganizationRolePermissions_RoleId_Permission" ON "OrganizationRolePermissions" ("RoleId", "Permission");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE UNIQUE INDEX "IX_OrganizationRoles_OrganizationId_Name" ON "OrganizationRoles" ("OrganizationId", "Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE INDEX "IX_OrganizationTeamMembers_RoleId" ON "OrganizationTeamMembers" ("RoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE UNIQUE INDEX "IX_OrganizationTeamMembers_TeamId_UserId" ON "OrganizationTeamMembers" ("TeamId", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE INDEX "IX_OrganizationTeamMembers_UserId" ON "OrganizationTeamMembers" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    CREATE UNIQUE INDEX "IX_OrganizationTeams_OrganizationId_Name" ON "OrganizationTeams" ("OrganizationId", "Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    ALTER TABLE "OrganizationMembers" ADD CONSTRAINT "FK_OrganizationMembers_OrganizationRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "OrganizationRoles" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513124459_OrganizationTeamsAndRoles') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260513124459_OrganizationTeamsAndRoles', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513211156_IdempotencyRecords') THEN
    CREATE TABLE "IdempotencyRecords" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "Key" character varying(120) NOT NULL,
        "Method" character varying(16) NOT NULL,
        "Path" character varying(1500) NOT NULL,
        "StatusCode" integer NOT NULL,
        "ResponseJson" jsonb,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_IdempotencyRecords" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513211156_IdempotencyRecords') THEN
    CREATE INDEX "IX_IdempotencyRecords_ExpiresAt" ON "IdempotencyRecords" ("ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513211156_IdempotencyRecords') THEN
    CREATE UNIQUE INDEX "IX_IdempotencyRecords_OrganizationId_Key" ON "IdempotencyRecords" ("OrganizationId", "Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260513211156_IdempotencyRecords') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260513211156_IdempotencyRecords', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514025236_AddProductIdentityDataSource') THEN
    ALTER TABLE "ProductIdentities" ADD "DataSource" text NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514025236_AddProductIdentityDataSource') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260514025236_AddProductIdentityDataSource', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514032241_OrganizationInviteCodes') THEN
    CREATE TABLE "OrganizationInviteCodes" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "TeamId" uuid,
        "RoleId" uuid,
        "CreatedByUserId" uuid,
        "Name" character varying(160) NOT NULL,
        "CodeHash" character varying(128) NOT NULL,
        "CodePrefix" character varying(24) NOT NULL,
        "IsActive" boolean NOT NULL,
        "ExpiresAt" timestamp with time zone,
        "MaxUses" integer,
        "UseCount" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_OrganizationInviteCodes" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_OrganizationInviteCodes_OrganizationRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "OrganizationRoles" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_OrganizationInviteCodes_OrganizationTeams_TeamId" FOREIGN KEY ("TeamId") REFERENCES "OrganizationTeams" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_OrganizationInviteCodes_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_OrganizationInviteCodes_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514032241_OrganizationInviteCodes') THEN
    CREATE UNIQUE INDEX "IX_OrganizationInviteCodes_CodeHash" ON "OrganizationInviteCodes" ("CodeHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514032241_OrganizationInviteCodes') THEN
    CREATE INDEX "IX_OrganizationInviteCodes_CreatedByUserId" ON "OrganizationInviteCodes" ("CreatedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514032241_OrganizationInviteCodes') THEN
    CREATE INDEX "IX_OrganizationInviteCodes_OrganizationId_IsActive" ON "OrganizationInviteCodes" ("OrganizationId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514032241_OrganizationInviteCodes') THEN
    CREATE INDEX "IX_OrganizationInviteCodes_RoleId" ON "OrganizationInviteCodes" ("RoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514032241_OrganizationInviteCodes') THEN
    CREATE INDEX "IX_OrganizationInviteCodes_TeamId" ON "OrganizationInviteCodes" ("TeamId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514032241_OrganizationInviteCodes') THEN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;

    WITH missing_owner_roles AS (
        SELECT o."Id" AS organization_id, gen_random_uuid() AS role_id
        FROM "Organizations" o
        WHERE NOT EXISTS (
            SELECT 1
            FROM "OrganizationRoles" r
            WHERE r."OrganizationId" = o."Id"
              AND r."IsOwnerRole" = TRUE
        )
    )
    INSERT INTO "OrganizationRoles" (
        "Id", "OrganizationId", "Name", "Description", "IsSystem", "IsOwnerRole", "CreatedAt", "UpdatedAt"
    )
    SELECT
        role_id,
        organization_id,
        'Owner',
        'Full workspace control, including owners, billing, roles, teams, and business data.',
        TRUE,
        TRUE,
        now(),
        now()
    FROM missing_owner_roles;

    INSERT INTO "OrganizationRolePermissions" (
        "Id", "RoleId", "Permission", "CreatedAt", "UpdatedAt"
    )
    SELECT gen_random_uuid(), r."Id", p.permission, now(), now()
    FROM "OrganizationRoles" r
    CROSS JOIN (VALUES
        ('organization.manage'),
        ('organization.owners.manage'),
        ('organization.roles.manage'),
        ('organization.teams.manage'),
        ('organization.members.manage'),
        ('inventory.manage'),
        ('routes.manage'),
        ('budgets.manage'),
        ('listings.manage'),
        ('tasks.manage'),
        ('reports.view')
    ) AS p(permission)
    WHERE r."IsOwnerRole" = TRUE
    ON CONFLICT ("RoleId", "Permission") DO NOTHING;

    UPDATE "OrganizationMembers" m
    SET "RoleId" = r."Id",
        "UpdatedAt" = now()
    FROM "OrganizationRoles" r
    WHERE m."OrganizationId" = r."OrganizationId"
      AND r."IsOwnerRole" = TRUE
      AND m."Role" = 'Owner'
      AND m."RoleId" IS NULL;

    WITH role_name_candidates AS (
        SELECT o."Id" AS organization_id, candidate.name, candidate.priority
        FROM "Organizations" o
        CROSS JOIN (VALUES
            ('Operator', 1),
            ('Team Operator', 2),
            ('Workspace Operator', 3)
        ) AS candidate(name, priority)
        WHERE NOT EXISTS (
            SELECT 1
            FROM "OrganizationRoles" existing_system
            WHERE existing_system."OrganizationId" = o."Id"
              AND existing_system."IsSystem" = TRUE
              AND existing_system."IsOwnerRole" = FALSE
        )
          AND NOT EXISTS (
            SELECT 1
            FROM "OrganizationRoles" conflicting_name
            WHERE conflicting_name."OrganizationId" = o."Id"
              AND conflicting_name."Name" = candidate.name
        )
    ),
    selected_operator_roles AS (
        SELECT DISTINCT ON (organization_id)
            organization_id,
            name,
            gen_random_uuid() AS role_id
        FROM role_name_candidates
        ORDER BY organization_id, priority
    )
    INSERT INTO "OrganizationRoles" (
        "Id", "OrganizationId", "Name", "Description", "IsSystem", "IsOwnerRole", "CreatedAt", "UpdatedAt"
    )
    SELECT
        role_id,
        organization_id,
        name,
        'Can run day-to-day sourcing, inventory, listings, budgets, tasks, and reports without owner administration.',
        TRUE,
        FALSE,
        now(),
        now()
    FROM selected_operator_roles;

    INSERT INTO "OrganizationRolePermissions" (
        "Id", "RoleId", "Permission", "CreatedAt", "UpdatedAt"
    )
    SELECT gen_random_uuid(), r."Id", p.permission, now(), now()
    FROM "OrganizationRoles" r
    CROSS JOIN (VALUES
        ('inventory.manage'),
        ('routes.manage'),
        ('budgets.manage'),
        ('listings.manage'),
        ('tasks.manage'),
        ('reports.view')
    ) AS p(permission)
    WHERE r."IsSystem" = TRUE
      AND r."IsOwnerRole" = FALSE
    ON CONFLICT ("RoleId", "Permission") DO NOTHING;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514032241_OrganizationInviteCodes') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260514032241_OrganizationInviteCodes', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "Accessories" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "Brand" character varying(160);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "Category" character varying(160);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "CleaningNotes" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "Color" character varying(120);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "ListingNotes" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "MeasurementsJson" jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "MissingParts" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "Model" character varying(160);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "SizeSystem" character varying(80);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "SizeValue" character varying(80);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "StorageLocation" character varying(160);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    ALTER TABLE "InventoryItems" ADD "TestingNotes" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    CREATE TABLE "InventoryPhotos" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "InventoryItemId" uuid NOT NULL,
        "IsHero" boolean NOT NULL,
        "SortOrder" integer NOT NULL,
        "OriginalFileName" character varying(240),
        "ContentType" character varying(80) NOT NULL,
        "OriginalBytes" bytea NOT NULL,
        "ThumbnailBytes" bytea NOT NULL,
        "OriginalByteSize" bigint NOT NULL,
        "ThumbnailByteSize" bigint NOT NULL,
        "Width" integer,
        "Height" integer,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_InventoryPhotos" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_InventoryPhotos_InventoryItems_InventoryItemId" FOREIGN KEY ("InventoryItemId") REFERENCES "InventoryItems" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_InventoryPhotos_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    CREATE TABLE "InventoryIssues" (
        "Id" uuid NOT NULL,
        "OrganizationId" uuid NOT NULL,
        "InventoryItemId" uuid NOT NULL,
        "InventoryPhotoId" uuid,
        "IssueType" character varying(80) NOT NULL,
        "Severity" character varying(40) NOT NULL,
        "Note" character varying(2000),
        "SortOrder" integer NOT NULL,
        "IsResolved" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_InventoryIssues" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_InventoryIssues_InventoryItems_InventoryItemId" FOREIGN KEY ("InventoryItemId") REFERENCES "InventoryItems" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_InventoryIssues_InventoryPhotos_InventoryPhotoId" FOREIGN KEY ("InventoryPhotoId") REFERENCES "InventoryPhotos" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_InventoryIssues_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    CREATE INDEX "IX_InventoryIssues_InventoryItemId" ON "InventoryIssues" ("InventoryItemId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    CREATE INDEX "IX_InventoryIssues_InventoryPhotoId" ON "InventoryIssues" ("InventoryPhotoId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    CREATE INDEX "IX_InventoryIssues_OrganizationId_InventoryItemId_IsResolved" ON "InventoryIssues" ("OrganizationId", "InventoryItemId", "IsResolved");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    CREATE INDEX "IX_InventoryPhotos_InventoryItemId_IsHero" ON "InventoryPhotos" ("InventoryItemId", "IsHero");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    CREATE INDEX "IX_InventoryPhotos_OrganizationId_InventoryItemId_SortOrder" ON "InventoryPhotos" ("OrganizationId", "InventoryItemId", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601224157_InventoryDataSafetyOverhaul') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260601224157_InventoryDataSafetyOverhaul', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601230206_InventoryAttributesJson') THEN
    ALTER TABLE "InventoryItems" ADD "AttributesJson" jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260601230206_InventoryAttributesJson') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260601230206_InventoryAttributesJson', '10.0.7');
    END IF;
END $EF$;
COMMIT;

