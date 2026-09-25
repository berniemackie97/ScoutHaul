using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HaulScout.Application.Common;
using HaulScout.Domain;
using HaulScout.Domain.Common;
using HaulScout.Domain.Entities;

namespace HaulScout.Infrastructure.Persistence;

/// <param name="clock">Injected in production; defaults to the system clock so hand-built test contexts need no ceremony.</param>
public sealed class HaulScoutDbContext(
    DbContextOptions<HaulScoutDbContext> options,
    ITenantContext? tenant = null,
    TimeProvider? clock = null) : DbContext(options)
{
    // Per-request tenant. Null at design-time (migrations) and in unit tests that construct
    // the context directly — in those cases org-scoping filters are left off (see ConfigureOrgFilter).
    private readonly ITenantContext? _tenant = tenant;

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ExternalAuthAccount> ExternalAuthAccounts => Set<ExternalAuthAccount>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<OrganizationRoleDefinition> OrganizationRoles => Set<OrganizationRoleDefinition>();
    public DbSet<OrganizationRolePermission> OrganizationRolePermissions => Set<OrganizationRolePermission>();
    public DbSet<OrganizationTeam> OrganizationTeams => Set<OrganizationTeam>();
    public DbSet<OrganizationTeamMember> OrganizationTeamMembers => Set<OrganizationTeamMember>();
    public DbSet<OrganizationInviteCode> OrganizationInviteCodes => Set<OrganizationInviteCode>();
    public DbSet<ProductIdentity> ProductIdentities => Set<ProductIdentity>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<AcquisitionLot> AcquisitionLots => Set<AcquisitionLot>();
    public DbSet<AcquisitionLotItem> AcquisitionLotItems => Set<AcquisitionLotItem>();
    public DbSet<InventoryBundleComponent> InventoryBundleComponents => Set<InventoryBundleComponent>();
    public DbSet<InventoryPhoto> InventoryPhotos => Set<InventoryPhoto>();
    public DbSet<InventoryIssue> InventoryIssues => Set<InventoryIssue>();
    public DbSet<MarketObservation> MarketObservations => Set<MarketObservation>();
    public DbSet<MarketAnalysis> MarketAnalyses => Set<MarketAnalysis>();
    public DbSet<BuyRecommendation> BuyRecommendations => Set<BuyRecommendation>();
    public DbSet<SourcingTrip> SourcingTrips => Set<SourcingTrip>();
    public DbSet<SourcingTripStop> SourcingTripStops => Set<SourcingTripStop>();
    public DbSet<SourcingStopChecklistItem> SourcingStopChecklistItems => Set<SourcingStopChecklistItem>();
    public DbSet<SourceLocation> SourceLocations => Set<SourceLocation>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleCorrection> SaleCorrections => Set<SaleCorrection>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<Return> Returns => Set<Return>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<ResaleTask> Tasks => Set<ResaleTask>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerChannelIdentity> CustomerChannelIdentities => Set<CustomerChannelIdentity>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<StoreCategory> StoreCategories => Set<StoreCategory>();
    public DbSet<StoreListing> StoreListings => Set<StoreListing>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartLine> CartLines => Set<CartLine>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<StorefrontOffer> StorefrontOffers => Set<StorefrontOffer>();
    public DbSet<ChannelAccount> ChannelAccounts => Set<ChannelAccount>();
    public DbSet<ChannelListing> ChannelListings => Set<ChannelListing>();
    public DbSet<OrgReferenceOverride> OrgReferenceOverrides => Set<OrgReferenceOverride>();
    public DbSet<PlatformOrder> PlatformOrders => Set<PlatformOrder>();
    public DbSet<PlatformListing> PlatformListings => Set<PlatformListing>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<SyncRunIssue> SyncRunIssues => Set<SyncRunIssue>();
    public DbSet<SyncArtifact> SyncArtifacts => Set<SyncArtifact>();
    public DbSet<SyncAgent> SyncAgents => Set<SyncAgent>();
    public DbSet<SyncAgentMarketplace> SyncAgentMarketplaces => Set<SyncAgentMarketplace>();
    public DbSet<SyncPairing> SyncPairings => Set<SyncPairing>();
    public DbSet<SyncLoop> SyncLoops => Set<SyncLoop>();
    public DbSet<SyncScanRequest> SyncScanRequests => Set<SyncScanRequest>();
    public DbSet<PlatformListingQualitySnapshot> PlatformListingQualitySnapshots => Set<PlatformListingQualitySnapshot>();
    public DbSet<PlatformOrderLine> PlatformOrderLines => Set<PlatformOrderLine>();

    private TimeProvider Clock => clock ?? TimeProvider.System;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // The storefront shelf follows the inventory automatically. SaveChanges is the one doorway every write
        // passes through, so the sync cannot be forgotten by a new code path the way a per-call-site sync would be.
        await Storefront.StoreListingSynchronizer.ApplyAsync(
            this, Storefront.StoreListingSynchronizer.CollectAffectedItemIds(ChangeTracker), Clock.GetUtcNow(), cancellationToken);
        ApplyTenantToNewEntities();
        StampEntities(ChangeTracker.Entries<EntityBase>());
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        Storefront.StoreListingSynchronizer.Apply(
            this, Storefront.StoreListingSynchronizer.CollectAffectedItemIds(ChangeTracker), Clock.GetUtcNow());
        ApplyTenantToNewEntities();
        StampEntities(ChangeTracker.Entries<EntityBase>());
        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("Organizations");
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.MarketplacePhotoSyncEnabled).HasDefaultValue(false);
            entity.Property(item => item.StoreDomain).HasMaxLength(253);
            entity.Property(item => item.TimeZoneId).HasMaxLength(64).IsRequired()
                .HasDefaultValue(Organization.DefaultTimeZoneId);
            entity.HasIndex(item => item.Name);
            entity.HasIndex(item => item.StoreDomain).IsUnique();
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(item => item.ExternalSubject).HasMaxLength(256);
            entity.Property(item => item.Email).HasMaxLength(320).IsRequired();
            entity.Property(item => item.DisplayName).HasMaxLength(160).IsRequired();
            entity.HasIndex(item => item.ExternalSubject).IsUnique();
            entity.HasIndex(item => item.Email).IsUnique();
        });

        modelBuilder.Entity<ExternalAuthAccount>(entity =>
        {
            entity.ToTable("ExternalAuthAccounts");
            entity.Property(item => item.Provider).HasMaxLength(80).IsRequired();
            entity.Property(item => item.ProviderAccountId).HasMaxLength(256).IsRequired();
            entity.Property(item => item.Email).HasMaxLength(320);
            entity.Property(item => item.DisplayName).HasMaxLength(160);
            entity.HasIndex(item => new { item.Provider, item.ProviderAccountId }).IsUnique();
            entity.HasIndex(item => item.UserId);
            entity.HasOne(item => item.User).WithMany(item => item.ExternalAccounts).HasForeignKey(item => item.UserId);
        });

        modelBuilder.Entity<OrganizationMember>(entity =>
        {
            entity.ToTable("OrganizationMembers");
            entity.Property(item => item.Role).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(item => new { item.OrganizationId, item.UserId }).IsUnique();
            entity.HasOne(item => item.Organization).WithMany(item => item.Members).HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.User).WithMany(item => item.Memberships).HasForeignKey(item => item.UserId);
            entity.HasOne(item => item.RoleDefinition).WithMany().HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrganizationRoleDefinition>(entity =>
        {
            entity.ToTable("OrganizationRoles");
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(500);
            entity.HasIndex(item => new { item.OrganizationId, item.Name }).IsUnique();
            entity.HasOne(item => item.Organization).WithMany(item => item.Roles).HasForeignKey(item => item.OrganizationId);
        });

        modelBuilder.Entity<OrganizationRolePermission>(entity =>
        {
            entity.ToTable("OrganizationRolePermissions");
            entity.Property(item => item.Permission).HasMaxLength(120).IsRequired();
            entity.HasIndex(item => new { item.RoleId, item.Permission }).IsUnique();
            entity.HasOne(item => item.Role).WithMany(item => item.Permissions).HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrganizationTeam>(entity =>
        {
            entity.ToTable("OrganizationTeams");
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(500);
            entity.HasIndex(item => new { item.OrganizationId, item.Name }).IsUnique();
            entity.HasOne(item => item.Organization).WithMany(item => item.Teams).HasForeignKey(item => item.OrganizationId);
        });

        modelBuilder.Entity<OrganizationTeamMember>(entity =>
        {
            entity.ToTable("OrganizationTeamMembers");
            entity.HasIndex(item => new { item.TeamId, item.UserId }).IsUnique();
            entity.HasOne(item => item.Team).WithMany(item => item.Members).HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId);
            entity.HasOne(item => item.RoleDefinition).WithMany().HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrganizationInviteCode>(entity =>
        {
            entity.ToTable("OrganizationInviteCodes");
            entity.Property(item => item.Name).HasMaxLength(160).IsRequired();
            entity.Property(item => item.CodeHash).HasMaxLength(128).IsRequired();
            entity.Property(item => item.CodePrefix).HasMaxLength(24).IsRequired();
            entity.HasIndex(item => item.CodeHash).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.IsActive });
            entity.HasOne(item => item.Organization).WithMany(item => item.InviteCodes).HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.Team).WithMany().HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.RoleDefinition).WithMany().HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.CreatedByUser).WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProductIdentity>(entity =>
        {
            entity.ToTable("ProductIdentities");
            entity.Property(item => item.Title).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Brand).HasMaxLength(160);
            entity.Property(item => item.Model).HasMaxLength(160);
            entity.Property(item => item.Category).HasMaxLength(160);
            entity.Property(item => item.Upc).HasMaxLength(32);
            entity.Property(item => item.Ean).HasMaxLength(32);
            entity.Property(item => item.Isbn).HasMaxLength(32);
            entity.Property(item => item.Sku).HasMaxLength(80);
            entity.Property(item => item.SearchQuery).HasMaxLength(500);
            entity.HasIndex(item => new { item.OrganizationId, item.Upc });
            entity.HasIndex(item => new { item.OrganizationId, item.Title });
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
        });

        modelBuilder.Entity<SourceLocation>(entity =>
        {
            entity.ToTable("SourceLocations");
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.LocationType).HasMaxLength(80);
            entity.Property(item => item.AddressLine1).HasMaxLength(240);
            entity.Property(item => item.City).HasMaxLength(120);
            entity.Property(item => item.Region).HasMaxLength(120);
            entity.Property(item => item.PostalCode).HasMaxLength(32);
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
        });

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("InventoryItems");
            entity.Property(item => item.Title).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Category).HasMaxLength(160);
            entity.Property(item => item.Brand).HasMaxLength(160);
            entity.Property(item => item.Model).HasMaxLength(160);
            entity.Property(item => item.Color).HasMaxLength(120);
            entity.Property(item => item.Condition).HasMaxLength(120);
            entity.Property(item => item.PhotoSourceKey).HasMaxLength(200);
            entity.Property(item => item.OwnershipType).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.FunctionalStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.CompletenessStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.AuthenticityStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.PackagingStatus).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.SizeSystem).HasMaxLength(80);
            entity.Property(item => item.SizeValue).HasMaxLength(80);
            entity.Property(item => item.ManufacturerPartNumber).HasMaxLength(120);
            entity.Property(item => item.SerialNumber).HasMaxLength(160);
            entity.Property(item => item.CountryOfOrigin).HasMaxLength(2);
            entity.Property(item => item.DimensionUnit).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.WeightUnit).HasConversion<string>().HasMaxLength(24);
            ConfigureMoney(entity.Property(item => item.Length));
            ConfigureMoney(entity.Property(item => item.Width));
            ConfigureMoney(entity.Property(item => item.Height));
            ConfigureMoney(entity.Property(item => item.Weight));
            entity.Property(item => item.AttributesJson).HasColumnType("jsonb");
            entity.Property(item => item.MeasurementsJson).HasColumnType("jsonb");
            entity.Property(item => item.StorageLocation).HasMaxLength(160);
            entity.Property(item => item.Barcode).HasMaxLength(80);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(40);
            ConfigureMoney(entity.Property(item => item.SellerAskingPrice));
            ConfigureMoney(entity.Property(item => item.PurchasePrice));
            ConfigureMoney(entity.Property(item => item.EstimatedSalePrice));
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasIndex(item => new { item.OrganizationId, item.IsArchived });
            entity.HasIndex(item => new { item.OrganizationId, item.IsBundle });
            entity.HasIndex(item => new { item.OrganizationId, item.Category });
            entity.HasIndex(item => new { item.OrganizationId, item.Brand });
            entity.HasIndex(item => new { item.OrganizationId, item.StorageLocation });
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.ProductIdentity).WithMany(item => item.InventoryItems).HasForeignKey(item => item.ProductIdentityId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.SourceLocation).WithMany().HasForeignKey(item => item.SourceLocationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.SourcingTrip).WithMany(item => item.Items).HasForeignKey(item => item.SourcingTripId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.SourcingTripStop).WithMany(item => item.Items).HasForeignKey(item => item.SourcingTripStopId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AcquisitionLot>(entity =>
        {
            entity.ToTable("AcquisitionLots");
            entity.Property(item => item.Name).HasMaxLength(240).IsRequired();
            entity.Property(item => item.SupplierName).HasMaxLength(240);
            entity.Property(item => item.ExternalReference).HasMaxLength(240);
            entity.Property(item => item.AllocationMethod).HasConversion<string>().HasMaxLength(40);
            ConfigureMoney(entity.Property(item => item.PurchaseSubtotal));
            ConfigureMoney(entity.Property(item => item.ShippingCost));
            ConfigureMoney(entity.Property(item => item.TaxCost));
            ConfigureMoney(entity.Property(item => item.FeeCost));
            ConfigureMoney(entity.Property(item => item.OtherCost));
            entity.HasIndex(item => new { item.OrganizationId, item.PurchasedAt });
            entity.HasIndex(item => new { item.OrganizationId, item.SupplierName });
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.SourceLocation).WithMany().HasForeignKey(item => item.SourceLocationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.SourcingTrip).WithMany().HasForeignKey(item => item.SourcingTripId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.SourcingTripStop).WithMany().HasForeignKey(item => item.SourcingTripStopId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AcquisitionLotItem>(entity =>
        {
            entity.ToTable("AcquisitionLotItems");
            ConfigureMoney(entity.Property(item => item.AllocatedCost));
            ConfigureMoney(entity.Property(item => item.AllocationWeight));
            entity.HasIndex(item => new { item.OrganizationId, item.AcquisitionLotId });
            entity.HasIndex(item => new { item.OrganizationId, item.InventoryItemId }).IsUnique();
            entity.HasOne(item => item.AcquisitionLot).WithMany(item => item.Items).HasForeignKey(item => item.AcquisitionLotId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.InventoryItem).WithMany(item => item.AcquisitionLotItems).HasForeignKey(item => item.InventoryItemId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryBundleComponent>(entity =>
        {
            entity.ToTable("InventoryBundleComponents");
            entity.Property(item => item.TitleSnapshot).HasMaxLength(300).IsRequired();
            ConfigureMoney(entity.Property(item => item.CostSnapshot));
            entity.HasIndex(item => new { item.OrganizationId, item.BundleInventoryItemId });
            entity.HasIndex(item => new { item.OrganizationId, item.ComponentInventoryItemId });
            entity.HasIndex(item => new { item.BundleInventoryItemId, item.ComponentInventoryItemId }).IsUnique();
            entity.HasOne(item => item.BundleInventoryItem)
                .WithMany(item => item.BundleComponents)
                .HasForeignKey(item => item.BundleInventoryItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.ComponentInventoryItem)
                .WithMany(item => item.ComponentOfBundles)
                .HasForeignKey(item => item.ComponentInventoryItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryPhoto>(entity =>
        {
            entity.ToTable("InventoryPhotos");
            entity.Property(item => item.OriginalFileName).HasMaxLength(240);
            entity.Property(item => item.ContentType).HasMaxLength(80).IsRequired();
            // Nullable because the images may live in object storage instead; exactly one of the two holds each image.
            entity.Property(item => item.OriginalObjectKey).HasMaxLength(400);
            entity.Property(item => item.ThumbnailObjectKey).HasMaxLength(400);
            entity.Property(item => item.SourceUrl).HasMaxLength(1000);
            // One photo per address on an item: two syncs racing cannot leave the item holding the same photo twice.
            entity.HasIndex(item => new { item.InventoryItemId, item.SourceUrl }).IsUnique().HasFilter("\"SourceUrl\" IS NOT NULL");
            entity.HasIndex(item => new { item.OrganizationId, item.InventoryItemId, item.SortOrder });
            entity.HasIndex(item => new { item.InventoryItemId, item.IsHero });
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.InventoryItem).WithMany(item => item.Photos).HasForeignKey(item => item.InventoryItemId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryIssue>(entity =>
        {
            entity.ToTable("InventoryIssues");
            entity.Property(item => item.IssueType).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Severity).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Note).HasMaxLength(2000);
            entity.HasIndex(item => new { item.OrganizationId, item.InventoryItemId, item.IsResolved });
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.InventoryItem).WithMany(item => item.Issues).HasForeignKey(item => item.InventoryItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.InventoryPhoto).WithMany().HasForeignKey(item => item.InventoryPhotoId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketObservation>(entity =>
        {
            entity.ToTable("MarketObservations");
            entity.Property(item => item.ObservationType).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.Source).HasMaxLength(80).IsRequired();
            entity.Property(item => item.SourceUrl).HasMaxLength(1500);
            entity.Property(item => item.Title).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Currency).HasMaxLength(8);
            entity.Property(item => item.Condition).HasMaxLength(120);
            ConfigureMoney(entity.Property(item => item.Price));
            ConfigureMoney(entity.Property(item => item.ShippingPrice));
            ConfigureMoney(entity.Property(item => item.TotalPrice));
            entity.HasIndex(item => new { item.OrganizationId, item.ObservationType });
            entity.HasIndex(item => new { item.ProductIdentityId, item.ObservationType });
            entity.HasIndex(item => new { item.InventoryItemId, item.ObservationType });
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.ProductIdentity).WithMany(item => item.MarketObservations).HasForeignKey(item => item.ProductIdentityId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.InventoryItem).WithMany(item => item.MarketObservations).HasForeignKey(item => item.InventoryItemId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketAnalysis>(entity =>
        {
            entity.ToTable("MarketAnalyses");
            entity.Property(item => item.PricingBasis).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.WarningsJson).HasColumnType("jsonb");
            entity.Property(item => item.ReasonsJson).HasColumnType("jsonb");
            ConfigureMoney(entity.Property(item => item.EstimatedSalePrice));
            entity.HasIndex(item => new { item.OrganizationId, item.CreatedAt });
        });

        modelBuilder.Entity<BuyRecommendation>(entity =>
        {
            entity.ToTable("BuyRecommendations");
            entity.Property(item => item.PricingBasis).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.Decision).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.WarningsJson).HasColumnType("jsonb");
            ConfigureMoney(entity.Property(item => item.RecommendedMaxBuyPrice));
            ConfigureMoney(entity.Property(item => item.ExpectedNetProfit));
            ConfigureMoney(entity.Property(item => item.BreakEvenPrice));
            entity.Property(item => item.RoiPercent).HasPrecision(9, 2);
            entity.Property(item => item.MarginPercent).HasPrecision(9, 2);
            entity.HasOne(item => item.MarketAnalysis).WithMany().HasForeignKey(item => item.MarketAnalysisId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SourcingTrip>(entity =>
        {
            entity.ToTable("SourcingTrips");
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.RouteType).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(40);
            ConfigureMoney(entity.Property(item => item.CashBudget));
            entity.HasIndex(item => new { item.OrganizationId, item.StartedAt });
            entity.HasIndex(item => new { item.OrganizationId, item.RouteDate });
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.AssignedToUser).WithMany().HasForeignKey(item => item.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SourcingTripStop>(entity =>
        {
            entity.ToTable("SourcingTripStops");
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.SourceType).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.Address).HasMaxLength(500).IsRequired();
            entity.Property(item => item.Phone).HasMaxLength(80);
            entity.Property(item => item.Url).HasMaxLength(1500);
            entity.Property(item => item.Priority).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            ConfigureMoney(entity.Property(item => item.BudgetCap));
            entity.HasIndex(item => new { item.OrganizationId, item.SourcingTripId, item.PlannedOrder });
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasOne(item => item.SourcingTrip).WithMany(item => item.Stops).HasForeignKey(item => item.SourcingTripId);
        });

        modelBuilder.Entity<SourcingStopChecklistItem>(entity =>
        {
            entity.ToTable("SourcingStopChecklistItems");
            entity.Property(item => item.Text).HasMaxLength(240).IsRequired();
            entity.HasIndex(item => new { item.OrganizationId, item.SourcingTripStopId, item.SortOrder });
            entity.HasOne(item => item.SourcingTripStop).WithMany(item => item.ChecklistItems).HasForeignKey(item => item.SourcingTripStopId);
        });

        modelBuilder.Entity<Listing>(entity =>
        {
            entity.ToTable("Listings");
            entity.Property(item => item.Platform).HasMaxLength(80).IsRequired();
            entity.Property(item => item.ListingUrl).HasMaxLength(1500);
            entity.Property(item => item.Title).HasMaxLength(ListingLimits.TitleMaxLength);
            entity.Property(item => item.Description).HasMaxLength(ListingLimits.DescriptionMaxLength);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            ConfigureMoney(entity.Property(item => item.ListPrice));
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasOne(item => item.InventoryItem).WithMany(item => item.Listings).HasForeignKey(item => item.InventoryItemId);
        });

        modelBuilder.Entity<Sale>(entity =>
        {
            entity.ToTable("Sales");
            entity.Property(item => item.Platform).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            ConfigureMoney(entity.Property(item => item.SoldPrice));
            ConfigureMoney(entity.Property(item => item.ShippingCharged));
            ConfigureMoney(entity.Property(item => item.CostOfGoods));
            ConfigureMoney(entity.Property(item => item.PlatformFees));
            ConfigureMoney(entity.Property(item => item.PaymentFees));
            ConfigureMoney(entity.Property(item => item.ShippingCost));
            ConfigureMoney(entity.Property(item => item.SuppliesCost));
            ConfigureMoney(entity.Property(item => item.OtherCosts));
            ConfigureMoney(entity.Property(item => item.SalesTaxCollected));
            ConfigureMoney(entity.Property(item => item.NetProfit));
            entity.Property(item => item.GroupOrderReference).HasMaxLength(120);
            entity.HasIndex(item => new { item.OrganizationId, item.SoldAt });
            entity.HasIndex(item => new { item.OrganizationId, item.SaleGroupId });
            entity.HasIndex(item => new { item.OrganizationId, item.OrderId });
            entity.HasOne(item => item.Order).WithMany().HasForeignKey(item => item.OrderId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.InventoryItem).WithMany(item => item.Sales).HasForeignKey(item => item.InventoryItemId);
            entity.HasOne(item => item.Listing).WithMany().HasForeignKey(item => item.ListingId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SaleCorrection>(entity =>
        {
            entity.ToTable("SaleCorrections");
            entity.Property(item => item.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.BeforeJson).HasColumnType("jsonb").IsRequired();
            entity.Property(item => item.AfterJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(item => new { item.OrganizationId, item.SaleId, item.Version });
            entity.HasOne(item => item.Sale).WithMany(item => item.Corrections).HasForeignKey(item => item.SaleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.CorrectedByUser).WithMany().HasForeignKey(item => item.CorrectedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Refund>(entity =>
        {
            entity.ToTable("Refunds");
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Notes).HasMaxLength(2000);
            entity.Property(item => item.ExternalRefundKey).HasMaxLength(200);
            entity.Ignore(item => item.BuyerRefundTotal);
            ConfigureMoney(entity.Property(item => item.ItemAmount));
            ConfigureMoney(entity.Property(item => item.ShippingRefunded));
            ConfigureMoney(entity.Property(item => item.TaxRefunded));
            ConfigureMoney(entity.Property(item => item.FeeCredit));
            entity.HasIndex(item => new { item.OrganizationId, item.SaleId });
            // The idempotency guarantee lives in the database: one refund per source identity, so a re-import
            // updates in place instead of duplicating, no matter what the application code does.
            entity.HasIndex(item => new { item.OrganizationId, item.ExternalRefundKey })
                .IsUnique()
                .HasFilter("\"ExternalRefundKey\" IS NOT NULL");
            entity.HasOne(item => item.Sale).WithMany(item => item.Refunds).HasForeignKey(item => item.SaleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.RecordedByUser).WithMany().HasForeignKey(item => item.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Return>(entity =>
        {
            entity.ToTable("Returns");
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Notes).HasMaxLength(2000);
            entity.Ignore(item => item.IsOpen);
            ConfigureMoney(entity.Property(item => item.ReturnShippingCost));
            entity.HasIndex(item => new { item.OrganizationId, item.SaleId });
            entity.HasIndex(item => new { item.OrganizationId, item.OrderId });
            entity.HasOne(item => item.Sale).WithMany(item => item.Returns).HasForeignKey(item => item.SaleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Refund).WithMany().HasForeignKey(item => item.RefundId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.RequestedByUser).WithMany().HasForeignKey(item => item.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.ToTable("Shipments");
            entity.Property(item => item.Carrier).HasMaxLength(80);
            entity.Property(item => item.TrackingNumber).HasMaxLength(160);
            ConfigureMoney(entity.Property(item => item.Cost));
            entity.HasIndex(item => new { item.OrganizationId, item.SaleId }).IsUnique();
            entity.HasOne(item => item.Sale).WithMany().HasForeignKey(item => item.SaleId);
        });

        modelBuilder.Entity<Expense>(entity =>
        {
            entity.ToTable("Expenses");
            entity.Property(item => item.Category).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.Description).HasMaxLength(300).IsRequired();
            ConfigureMoney(entity.Property(item => item.Amount));
            entity.HasIndex(item => new { item.OrganizationId, item.OccurredAt });
            entity.HasIndex(item => new { item.OrganizationId, item.AcquisitionLotId });
            entity.HasOne(item => item.AcquisitionLot).WithMany().HasForeignKey(item => item.AcquisitionLotId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<SourcingTripStop>().WithMany().HasForeignKey(item => item.SourcingTripStopId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Budget>(entity =>
        {
            entity.ToTable("Budgets");
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            ConfigureMoney(entity.Property(item => item.StartingAmount));
            ConfigureMoney(entity.Property(item => item.CurrentAmount));
            entity.HasIndex(item => new { item.OrganizationId, item.IsActive });
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
        });

        modelBuilder.Entity<ResaleTask>(entity =>
        {
            entity.ToTable("Tasks");
            entity.Property(item => item.Title).HasMaxLength(240).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasOne(item => item.AssignedToUser).WithMany().HasForeignKey(item => item.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.InventoryItem).WithMany().HasForeignKey(item => item.InventoryItemId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.Property(item => item.EntityName).HasMaxLength(160).IsRequired();
            entity.Property(item => item.Action).HasMaxLength(80).IsRequired();
            entity.Property(item => item.DetailsJson).HasColumnType("jsonb");
            entity.HasIndex(item => new { item.OrganizationId, item.CreatedAt });
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.Property(item => item.Key).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Method).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Path).HasMaxLength(1500).IsRequired();
            entity.Property(item => item.ResponseJson).HasColumnType("jsonb");
            entity.HasIndex(item => new { item.OrganizationId, item.Key }).IsUnique();
            entity.HasIndex(item => item.ExpiresAt);
        });

        // ── Storefront commerce ──────────────────────────────────────
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.Property(item => item.ExternalSubject).HasMaxLength(256);
            // Nullable: a marketplace buyer is identified by their channel username (CustomerChannelIdentity).
            entity.Property(item => item.Email).HasMaxLength(320);
            entity.Property(item => item.DisplayName).HasMaxLength(160);
            entity.Property(item => item.Phone).HasMaxLength(40);
            ConfigureMoney(entity.Property(item => item.StoreCreditBalance));
            entity.HasIndex(item => new { item.OrganizationId, item.Email })
                .IsUnique()
                .HasFilter("\"Email\" IS NOT NULL");
            entity.HasIndex(item => new { item.OrganizationId, item.ExternalSubject })
                .IsUnique()
                .HasFilter("\"ExternalSubject\" IS NOT NULL");
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
        });

        modelBuilder.Entity<CustomerChannelIdentity>(entity =>
        {
            entity.ToTable("CustomerChannelIdentities");
            entity.Property(item => item.Channel).HasMaxLength(60).IsRequired();
            entity.Property(item => item.ExternalUserId).HasMaxLength(200).IsRequired();
            entity.Property(item => item.DisplayName).HasMaxLength(200);
            entity.Property(item => item.Email).HasMaxLength(320);
            // One buyer handle per channel, per org — this is what dedupes a repeat buyer across orders.
            entity.HasIndex(item => new { item.OrganizationId, item.Channel, item.ExternalUserId }).IsUnique();
            entity.HasOne(item => item.Customer).WithMany(item => item.ChannelIdentities)
                .HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerAddress>(entity =>
        {
            entity.ToTable("CustomerAddresses");
            entity.Property(item => item.Label).HasMaxLength(80);
            entity.Property(item => item.RecipientName).HasMaxLength(160).IsRequired();
            entity.Property(item => item.Line1).HasMaxLength(240).IsRequired();
            entity.Property(item => item.Line2).HasMaxLength(240);
            entity.Property(item => item.City).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Region).HasMaxLength(120).IsRequired();
            entity.Property(item => item.PostalCode).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Country).HasMaxLength(2).IsRequired();
            entity.Property(item => item.Phone).HasMaxLength(40);
            entity.HasIndex(item => new { item.OrganizationId, item.CustomerId });
            entity.HasOne(item => item.Customer).WithMany(item => item.Addresses).HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StoreCategory>(entity =>
        {
            entity.ToTable("StoreCategories");
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Slug).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(2000);
            entity.Property(item => item.ImageKey).HasMaxLength(500);
            entity.HasIndex(item => new { item.OrganizationId, item.Slug }).IsUnique();
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.Parent).WithMany(item => item.Children).HasForeignKey(item => item.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StoreListing>(entity =>
        {
            entity.ToTable("StoreListings");
            entity.Property(item => item.Slug).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Subtitle).HasMaxLength(300);
            entity.Property(item => item.MetaTitle).HasMaxLength(200);
            entity.Property(item => item.MetaDescription).HasMaxLength(500);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.AcceptsOffers).HasDefaultValue(true);
            entity.Property(item => item.ShippingPolicy).HasConversion<string>().HasMaxLength(32).HasDefaultValue(StoreShippingPolicy.StoreDefault);
            ConfigureMoney(entity.Property(item => item.ListPrice));
            ConfigureMoney(entity.Property(item => item.CompareAtPrice));
            ConfigureMoney(entity.Property(item => item.MinimumOfferAmount));
            ConfigureMoney(entity.Property(item => item.MaximumOfferAmount));
            ConfigureMoney(entity.Property(item => item.ShippingAmountOverride));
            entity.HasIndex(item => new { item.OrganizationId, item.Slug }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasIndex(item => item.InventoryItemId).IsUnique();
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.InventoryItem).WithOne(item => item.StoreListing).HasForeignKey<StoreListing>(item => item.InventoryItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.StoreCategory).WithMany(item => item.Listings).HasForeignKey(item => item.StoreCategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Cart>(entity =>
        {
            entity.ToTable("Carts");
            entity.Property(item => item.AnonymousId).HasMaxLength(80);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(item => new { item.OrganizationId, item.AnonymousId });
            entity.HasIndex(item => new { item.OrganizationId, item.CustomerId });
            entity.HasOne(item => item.Customer).WithMany().HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CartLine>(entity =>
        {
            entity.ToTable("CartLines");
            ConfigureMoney(entity.Property(item => item.UnitPriceSnapshot));
            entity.HasIndex(item => new { item.OrganizationId, item.CartId });
            entity.HasIndex(item => new { item.OrganizationId, item.CartId, item.StoreListingId }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.StorefrontOfferId });
            entity.HasOne(item => item.Cart).WithMany(item => item.Lines).HasForeignKey(item => item.CartId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.StoreListing).WithMany().HasForeignKey(item => item.StoreListingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.StorefrontOffer).WithMany().HasForeignKey(item => item.StorefrontOfferId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.Property(item => item.OrderNumber).HasMaxLength(40).IsRequired();
            // Nullable: marketplace buyers have a username, not an email. See Order.BuyerReference.
            entity.Property(item => item.Email).HasMaxLength(320);
            entity.Property(item => item.Channel).HasMaxLength(60).IsRequired();
            entity.Property(item => item.ExternalOrderId).HasMaxLength(120);
            entity.Property(item => item.ExternalOrderUrl).HasMaxLength(500);
            entity.Property(item => item.BuyerReference).HasMaxLength(200);
            entity.HasIndex(item => new { item.OrganizationId, item.Channel });
            // One order per marketplace order id, per channel. Storefront rows leave ExternalOrderId null.
            entity.HasIndex(item => new { item.OrganizationId, item.Channel, item.ExternalOrderId })
                .IsUnique()
                .HasFilter("\"ExternalOrderId\" IS NOT NULL");
            entity.HasOne(item => item.ChannelAccount).WithMany().HasForeignKey(item => item.ChannelAccountId).OnDelete(DeleteBehavior.SetNull);
            entity.Property(item => item.Currency).HasMaxLength(8);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.PaymentStatus).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.FulfillmentStatus).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.StripeCheckoutSessionId).HasMaxLength(120);
            entity.Property(item => item.StripePaymentIntentId).HasMaxLength(120);
            // The webhook's only lookup key, and the resume path's — both hit these cold.
            entity.HasIndex(item => item.StripeCheckoutSessionId);
            entity.HasIndex(item => item.SourceCartId);
            entity.Property(item => item.ShipToName).HasMaxLength(160);
            entity.Property(item => item.ShipToLine1).HasMaxLength(240);
            entity.Property(item => item.ShipToLine2).HasMaxLength(240);
            entity.Property(item => item.ShipToCity).HasMaxLength(120);
            entity.Property(item => item.ShipToRegion).HasMaxLength(120);
            entity.Property(item => item.ShipToPostalCode).HasMaxLength(32);
            entity.Property(item => item.ShipToCountry).HasMaxLength(2);
            entity.Property(item => item.CustomerNote).HasMaxLength(2000);
            entity.Property(item => item.ShippingCarrier).HasMaxLength(80);
            entity.Property(item => item.TrackingNumber).HasMaxLength(160);
            entity.Property(item => item.TrackingUrl).HasMaxLength(1500);
            ConfigureMoney(entity.Property(item => item.Subtotal));
            ConfigureMoney(entity.Property(item => item.DiscountTotal));
            ConfigureMoney(entity.Property(item => item.TaxTotal));
            ConfigureMoney(entity.Property(item => item.ShippingTotal));
            ConfigureMoney(entity.Property(item => item.GrandTotal));
            entity.HasIndex(item => new { item.OrganizationId, item.OrderNumber }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasIndex(item => new { item.OrganizationId, item.CreatedAt });
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.Customer).WithMany(item => item.Orders).HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("OrderLines");
            entity.Property(item => item.TitleSnapshot).HasMaxLength(300).IsRequired();
            entity.Property(item => item.SkuSnapshot).HasMaxLength(80);
            ConfigureMoney(entity.Property(item => item.UnitPrice));
            ConfigureMoney(entity.Property(item => item.LineTotal));
            ConfigureMoney(entity.Property(item => item.UnitCostSnapshot));
            entity.HasIndex(item => new { item.OrganizationId, item.OrderId });
            entity.HasIndex(item => new { item.OrganizationId, item.StorefrontOfferId });
            entity.HasOne(item => item.Order).WithMany(item => item.Lines).HasForeignKey(item => item.OrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.InventoryItem).WithMany().HasForeignKey(item => item.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.StorefrontOffer).WithMany().HasForeignKey(item => item.StorefrontOfferId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WishlistItem>(entity =>
        {
            entity.ToTable("WishlistItems");
            entity.HasIndex(item => new { item.OrganizationId, item.CustomerId, item.StoreListingId }).IsUnique();
            entity.HasOne(item => item.Customer).WithMany(item => item.WishlistItems).HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.StoreListing).WithMany().HasForeignKey(item => item.StoreListingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.ToTable("Reviews");
            entity.Property(item => item.Title).HasMaxLength(200);
            entity.Property(item => item.Body).HasMaxLength(4000);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(item => new { item.OrganizationId, item.StoreListingId });
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasOne(item => item.Customer).WithMany(item => item.Reviews).HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.StoreListing).WithMany(item => item.Reviews).HasForeignKey(item => item.StoreListingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StorefrontOffer>(entity =>
        {
            entity.ToTable("StorefrontOffers");
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            ConfigureMoney(entity.Property(item => item.OfferAmount));
            ConfigureMoney(entity.Property(item => item.ListingPriceSnapshot));
            ConfigureMoney(entity.Property(item => item.ResponseAmount));
            entity.Property(item => item.BuyerName).HasMaxLength(160);
            entity.Property(item => item.BuyerEmail).HasMaxLength(320);
            entity.Property(item => item.BuyerPhone).HasMaxLength(40);
            entity.Property(item => item.Message).HasMaxLength(2000);
            entity.Property(item => item.ResponseMessage).HasMaxLength(2000);
            entity.Property(item => item.ResponseNotes).HasMaxLength(2000);
            entity.HasIndex(item => new { item.OrganizationId, item.CustomerId });
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasIndex(item => new { item.OrganizationId, item.StoreListingId, item.CreatedAt });
            entity.HasIndex(item => new { item.OrganizationId, item.BuyerEmail });
            entity.HasOne(item => item.Customer).WithMany().HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.StoreListing).WithMany().HasForeignKey(item => item.StoreListingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.InventoryItem).WithMany().HasForeignKey(item => item.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChannelAccount>(entity =>
        {
            entity.ToTable("ChannelAccounts");
            entity.Property(item => item.Channel).HasMaxLength(60).IsRequired();
            entity.Property(item => item.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(item => item.ExternalAccountId).HasMaxLength(200);
            entity.Property(item => item.DefaultFeeProfileId).HasMaxLength(80);
            entity.Property(item => item.Notes).HasMaxLength(2000);
            entity.Property(item => item.BoundUsername).HasMaxLength(120);
            entity.Property(item => item.BoundMarketplaceUserId).HasMaxLength(120);
            entity.HasIndex(item => new { item.OrganizationId, item.Channel });
            entity.HasIndex(item => new { item.OrganizationId, item.DisplayName }).IsUnique();
        });

        modelBuilder.Entity<ChannelListing>(entity =>
        {
            entity.ToTable("ChannelListings");
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.ExternalListingId).HasMaxLength(200);
            entity.Property(item => item.ExternalUrl).HasMaxLength(1000);
            entity.Property(item => item.ImageHash).HasMaxLength(128);
            entity.Property(item => item.LastError).HasMaxLength(2000);
            ConfigureMoney(entity.Property(item => item.ListedPrice));
            // One listing row per item per channel account.
            entity.HasIndex(item => new { item.OrganizationId, item.InventoryItemId, item.ChannelAccountId }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasOne(item => item.InventoryItem).WithMany(item => item.ChannelListings).HasForeignKey(item => item.InventoryItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.ChannelAccount).WithMany(item => item.Listings).HasForeignKey(item => item.ChannelAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrgReferenceOverride>(entity =>
        {
            entity.ToTable("OrgReferenceOverrides");
            entity.Property(item => item.Kind).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Value).HasMaxLength(200).IsRequired();
            entity.Property(item => item.ParentValue).HasMaxLength(200);
            entity.Property(item => item.Label).HasMaxLength(200);
            entity.HasIndex(item => new { item.OrganizationId, item.Kind, item.Value, item.ParentValue }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.Kind });
        });

        modelBuilder.Entity<PlatformOrder>(entity =>
        {
            entity.ToTable("PlatformOrders");
            entity.Property(item => item.FeesPending).HasDefaultValue(false);
            entity.Property(item => item.Platform).HasMaxLength(60).IsRequired();
            entity.Property(item => item.ExternalOrderId).HasMaxLength(120).IsRequired();
            entity.Property(item => item.BuyerName).HasMaxLength(200);
            entity.Property(item => item.BuyerReference).HasMaxLength(200);
            entity.Property(item => item.BuyerEmail).HasMaxLength(320);
            entity.Property(item => item.BuyerNote).HasMaxLength(2000);
            entity.Property(item => item.ShipToName).HasMaxLength(160);
            entity.Property(item => item.ShipToPhone).HasMaxLength(40);
            entity.Property(item => item.ShipToLine1).HasMaxLength(240);
            entity.Property(item => item.ShipToLine2).HasMaxLength(240);
            entity.Property(item => item.ShipToCity).HasMaxLength(120);
            entity.Property(item => item.ShipToRegion).HasMaxLength(120);
            entity.Property(item => item.ShipToPostalCode).HasMaxLength(32);
            entity.Property(item => item.ShipToCountry).HasMaxLength(80);
            entity.Property(item => item.Currency).HasMaxLength(3);
            entity.Property(item => item.Carrier).HasMaxLength(60);
            entity.Property(item => item.TrackingNumber).HasMaxLength(120);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.FeeBreakdownJson).HasColumnType("jsonb");
            entity.Property(item => item.RawJson).HasColumnType("jsonb");
            ConfigureMoney(entity.Property(item => item.ItemsSubtotal));
            ConfigureMoney(entity.Property(item => item.ShippingCharged));
            ConfigureMoney(entity.Property(item => item.TaxCollected));
            ConfigureMoney(entity.Property(item => item.FeeTotal));
            ConfigureMoney(entity.Property(item => item.SellerShippingCost));
            ConfigureMoney(entity.Property(item => item.PayoutAmount));
            entity.HasIndex(item => new { item.OrganizationId, item.Platform, item.ExternalOrderId }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
        });

        modelBuilder.Entity<PlatformOrderLine>(entity =>
        {
            entity.ToTable("PlatformOrderLines");
            entity.Property(item => item.Title).HasMaxLength(400).IsRequired();
            entity.Property(item => item.ExternalItemId).HasMaxLength(120);
            entity.Property(item => item.Sku).HasMaxLength(120);
            ConfigureMoney(entity.Property(item => item.UnitPrice));
            ConfigureMoney(entity.Property(item => item.ShippingCharged));
            ConfigureMoney(entity.Property(item => item.TaxCollected));
            ConfigureMoney(entity.Property(item => item.PlatformFees));
            ConfigureMoney(entity.Property(item => item.SellerShippingCost));
            ConfigureMoney(entity.Property(item => item.NetSales));
            ConfigureMoney(entity.Property(item => item.TotalSales));
            entity.Property(item => item.FeeBreakdownJson).HasColumnType("jsonb");
            entity.Property(item => item.RawJson).HasColumnType("jsonb");
            entity.HasIndex(item => item.PlatformOrderId);
            entity.HasOne(item => item.PlatformOrder).WithMany(item => item.Lines).HasForeignKey(item => item.PlatformOrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.InventoryItem).WithMany().HasForeignKey(item => item.InventoryItemId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SyncRun>(entity =>
        {
            entity.ToTable("SyncRuns");
            entity.Property(item => item.FormatKey).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Platform).HasMaxLength(60).IsRequired();
            entity.Property(item => item.Source).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.Summary).HasMaxLength(500);
            // History is read newest first, per organization; that is the only list query.
            entity.HasIndex(item => new { item.OrganizationId, item.StartedAt });
            entity.Property(item => item.Loop).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Trigger).HasConversion<string>().HasMaxLength(16);
            entity.HasOne(item => item.ChannelAccount).WithMany().HasForeignKey(item => item.ChannelAccountId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.Artifact).WithMany().HasForeignKey(item => item.ArtifactId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.SyncAgent).WithMany().HasForeignKey(item => item.SyncAgentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(item => item.Issues).WithOne(item => item.SyncRun).HasForeignKey(item => item.SyncRunId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SyncAgent>(entity =>
        {
            entity.ToTable("SyncAgents");
            entity.Property(item => item.Name).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Browser).HasMaxLength(40).IsRequired();
            entity.Property(item => item.OperatingSystem).HasMaxLength(40);
            entity.Property(item => item.ExtensionVersion).HasMaxLength(24).IsRequired();
            entity.Property(item => item.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(item => item.PreviousTokenHash).HasMaxLength(64);
            // A token is looked up before anyone knows its organization, so its hash is unique everywhere.
            entity.HasIndex(item => item.TokenHash).IsUnique();
            entity.HasIndex(item => item.PreviousTokenHash);
            entity.HasIndex(item => item.OrganizationId);
            entity.HasOne(item => item.PairedByUser).WithMany().HasForeignKey(item => item.PairedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(item => item.Marketplaces).WithOne(item => item.SyncAgent).HasForeignKey(item => item.SyncAgentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SyncAgentMarketplace>(entity =>
        {
            entity.ToTable("SyncAgentMarketplaces");
            entity.Property(item => item.Platform).HasMaxLength(60).IsRequired();
            entity.Property(item => item.Username).HasMaxLength(120);
            entity.Property(item => item.MarketplaceUserId).HasMaxLength(120);
            entity.Property(item => item.PauseReason).HasMaxLength(300);
            entity.HasIndex(item => new { item.SyncAgentId, item.Platform }).IsUnique();
        });

        modelBuilder.Entity<SyncPairing>(entity =>
        {
            entity.ToTable("SyncPairings");
            entity.Property(item => item.CodeHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(item => item.CodeHash).IsUnique();
            entity.HasIndex(item => item.OrganizationId);
        });

        modelBuilder.Entity<SyncLoop>(entity =>
        {
            entity.ToTable("SyncLoops");
            entity.Property(item => item.Kind).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Cursor).HasMaxLength(400);
            entity.Property(item => item.LastError).HasMaxLength(500);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ChannelAccountId, item.Kind }).IsUnique();
            entity.HasIndex(item => item.OrganizationId);
            entity.HasOne(item => item.ChannelAccount).WithMany().HasForeignKey(item => item.ChannelAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SyncScanRequest>(entity =>
        {
            entity.ToTable("SyncScanRequests");
            entity.Property(item => item.Loop).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Message).HasMaxLength(500);
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasIndex(item => new { item.ChannelAccountId, item.Loop, item.Status });
            entity.HasOne(item => item.ChannelAccount).WithMany().HasForeignKey(item => item.ChannelAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SyncRunIssue>(entity =>
        {
            entity.ToTable("SyncRunIssues");
            entity.Property(item => item.Severity).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.ExternalReference).HasMaxLength(200);
            entity.Property(item => item.Message).HasMaxLength(1000).IsRequired();
            entity.HasIndex(item => item.SyncRunId);
        });

        modelBuilder.Entity<SyncArtifact>(entity =>
        {
            entity.ToTable("SyncArtifacts");
            entity.Property(item => item.FileName).HasMaxLength(260).IsRequired();
            entity.Property(item => item.ContentType).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Sha256).HasMaxLength(64).IsRequired();
            // "This exact file was imported before" is a lookup by hash within an organization.
            entity.HasIndex(item => new { item.OrganizationId, item.Sha256 });
        });

        modelBuilder.Entity<PlatformListing>(entity =>
        {
            entity.ToTable("PlatformListings");
            entity.Property(item => item.Platform).HasMaxLength(60).IsRequired();
            entity.Property(item => item.ExternalListingId).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(400).IsRequired();
            entity.Property(item => item.Currency).HasMaxLength(3);
            entity.Property(item => item.Format).HasMaxLength(24);
            entity.Property(item => item.Sku).HasMaxLength(120);
            entity.Property(item => item.CategoryName).HasMaxLength(200);
            entity.Property(item => item.Condition).HasMaxLength(80);
            entity.Property(item => item.Url).HasMaxLength(500);
            entity.Property(item => item.PrimaryImageUrl).HasMaxLength(500);
            entity.Property(item => item.PhotoUrlsJson).HasColumnType("jsonb");
            entity.Property(item => item.PhotoUrlsKey).HasMaxLength(80);
            entity.Property(item => item.PhotoSyncedKey).HasMaxLength(80);
            entity.Property(item => item.PhotoSyncError).HasMaxLength(500);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.ReconcileStatus).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.CreatedChannelListingOnReconcile).HasDefaultValue(false);
            entity.Property(item => item.RawJson).HasColumnType("jsonb");
            ConfigureMoney(entity.Property(item => item.Price));
            entity.HasIndex(item => new { item.OrganizationId, item.Platform, item.ExternalListingId }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.ReconcileStatus });
            entity.HasOne(item => item.ReconciledInventoryItem).WithMany().HasForeignKey(item => item.ReconciledInventoryItemId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PlatformListingQualitySnapshot>(entity =>
        {
            entity.ToTable("PlatformListingQualitySnapshots");
            entity.Property(item => item.Platform).HasMaxLength(60).IsRequired();
            entity.Property(item => item.ExternalListingId).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(400).IsRequired();
            entity.Property(item => item.CategoryName).HasMaxLength(200);
            entity.Property(item => item.ListingCondition).HasMaxLength(80);
            entity.Property(item => item.PriceRange).HasMaxLength(120);
            entity.Property(item => item.Action).HasMaxLength(80);
            entity.Property(item => item.RecommendedItemSpecificsToAdd).HasMaxLength(1000);
            entity.Property(item => item.Brand).HasMaxLength(160);
            entity.Property(item => item.Mpn).HasMaxLength(160);
            entity.Property(item => item.Upc).HasMaxLength(80);
            entity.Property(item => item.RawJson).HasColumnType("jsonb");
            entity.Property(item => item.DailyImpressions).HasPrecision(18, 4);
            entity.Property(item => item.ClickThroughRate).HasPrecision(9, 6);
            entity.Property(item => item.SalesConversionRate).HasPrecision(9, 6);
            entity.Property(item => item.PromotedListingsAdRate).HasPrecision(9, 6);
            entity.HasIndex(item => new { item.OrganizationId, item.Platform, item.ExternalListingId, item.ReportGeneratedAt }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.PlatformListingId, item.ReportGeneratedAt });
            entity.HasOne(item => item.PlatformListing).WithMany().HasForeignKey(item => item.PlatformListingId).OnDelete(DeleteBehavior.SetNull);
        });

        ApplyOrganizationQueryFilters(modelBuilder);
    }

    // Auto-assigns the active organization to newly added org-scoped entities that did not set one,
    // so callers cannot accidentally persist a row without (or with a wrong) OrganizationId. No-op
    // when there is no tenant (design-time / tests) or the org has not been resolved yet.
    private void ApplyTenantToNewEntities()
    {
        if (_tenant?.OrganizationId is not Guid organizationId)
        {
            return;
        }

        foreach (EntityEntry<IOrganizationScoped> entry in ChangeTracker.Entries<IOrganizationScoped>())
        {
            if (entry.State == EntityState.Added && entry.Entity.OrganizationId == Guid.Empty)
            {
                entry.Entity.OrganizationId = organizationId;
            }
        }
    }

    private static readonly MethodInfo ConfigureOrgFilterMethod =
        typeof(HaulScoutDbContext).GetMethod(nameof(ConfigureOrgFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    // Applies org-scoping query filters to user-facing business data only. IdempotencyRecord and
    // AuditLog are infrastructure records written/read by middleware with an explicit OrganizationId
    // and are intentionally excluded so cross-cutting behavior is not affected by the request tenant.
    private void ApplyOrganizationQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            Type clrType = entityType.ClrType;
            if (!typeof(IOrganizationScoped).IsAssignableFrom(clrType))
            {
                continue;
            }

            if (clrType == typeof(IdempotencyRecord) || clrType == typeof(AuditLog))
            {
                continue;
            }

            ConfigureOrgFilterMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
        }
    }

    // Resolves the value compared against each row's OrganizationId. Routed through a null-safe
    // property (not a raw _tenant deref) because EF eagerly evaluates member accesses during query
    // parameter extraction, ignoring the boolean short-circuit below:
    //   - no tenant service at all (design-time / unit tests) => null => filter left off (unfiltered)
    //   - tenant present but org not yet resolved              => Guid.Empty => matches no real row (fail closed)
    //   - tenant resolved                                      => the active organization id (scoped)
    private Guid? TenantFilterId => _tenant is null ? null : _tenant.OrganizationId ?? Guid.Empty;

    private void ConfigureOrgFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, IOrganizationScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => TenantFilterId == null || entity.OrganizationId == TenantFilterId);
    }

    private static void StampEntities(IEnumerable<EntityEntry<EntityBase>> entries)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (EntityEntry<EntityBase> entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    private static void ConfigureMoney(PropertyBuilder<decimal> property) => property.HasPrecision(12, 2);
    private static void ConfigureMoney(PropertyBuilder<decimal?> property) => property.HasPrecision(12, 2);
}
