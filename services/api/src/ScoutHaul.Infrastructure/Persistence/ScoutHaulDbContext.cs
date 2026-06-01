using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScoutHaul.Domain.Common;
using ScoutHaul.Domain.Entities;

namespace ScoutHaul.Infrastructure.Persistence;

public sealed class ScoutHaulDbContext(DbContextOptions<ScoutHaulDbContext> options) : DbContext(options)
{
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
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<ResaleTask> Tasks => Set<ResaleTask>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampEntities(ChangeTracker.Entries<EntityBase>());
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampEntities(ChangeTracker.Entries<EntityBase>());
        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("Organizations");
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(item => item.Name);
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
            entity.Property(item => item.SizeSystem).HasMaxLength(80);
            entity.Property(item => item.SizeValue).HasMaxLength(80);
            entity.Property(item => item.MeasurementsJson).HasColumnType("jsonb");
            entity.Property(item => item.StorageLocation).HasMaxLength(160);
            entity.Property(item => item.Barcode).HasMaxLength(80);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(40);
            ConfigureMoney(entity.Property(item => item.SellerAskingPrice));
            ConfigureMoney(entity.Property(item => item.PurchasePrice));
            ConfigureMoney(entity.Property(item => item.EstimatedSalePrice));
            entity.HasIndex(item => new { item.OrganizationId, item.Status });
            entity.HasIndex(item => new { item.OrganizationId, item.IsArchived });
            entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId);
            entity.HasOne(item => item.ProductIdentity).WithMany(item => item.InventoryItems).HasForeignKey(item => item.ProductIdentityId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.SourceLocation).WithMany().HasForeignKey(item => item.SourceLocationId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.SourcingTrip).WithMany(item => item.Items).HasForeignKey(item => item.SourcingTripId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.SourcingTripStop).WithMany(item => item.Items).HasForeignKey(item => item.SourcingTripStopId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InventoryPhoto>(entity =>
        {
            entity.ToTable("InventoryPhotos");
            entity.Property(item => item.OriginalFileName).HasMaxLength(240);
            entity.Property(item => item.ContentType).HasMaxLength(80).IsRequired();
            entity.Property(item => item.OriginalBytes).IsRequired();
            entity.Property(item => item.ThumbnailBytes).IsRequired();
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
            ConfigureMoney(entity.Property(item => item.PlatformFees));
            ConfigureMoney(entity.Property(item => item.PaymentFees));
            ConfigureMoney(entity.Property(item => item.ShippingCost));
            ConfigureMoney(entity.Property(item => item.SuppliesCost));
            ConfigureMoney(entity.Property(item => item.OtherCosts));
            ConfigureMoney(entity.Property(item => item.NetProfit));
            entity.HasIndex(item => new { item.OrganizationId, item.SoldAt });
            entity.HasOne(item => item.InventoryItem).WithMany(item => item.Sales).HasForeignKey(item => item.InventoryItemId);
            entity.HasOne(item => item.Listing).WithMany().HasForeignKey(item => item.ListingId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.ToTable("Shipments");
            entity.Property(item => item.Carrier).HasMaxLength(80);
            entity.Property(item => item.TrackingNumber).HasMaxLength(160);
            ConfigureMoney(entity.Property(item => item.Cost));
            entity.HasOne(item => item.Sale).WithMany().HasForeignKey(item => item.SaleId);
        });

        modelBuilder.Entity<Expense>(entity =>
        {
            entity.ToTable("Expenses");
            entity.Property(item => item.Category).HasConversion<string>().HasMaxLength(40);
            entity.Property(item => item.Description).HasMaxLength(300).IsRequired();
            ConfigureMoney(entity.Property(item => item.Amount));
            entity.HasIndex(item => new { item.OrganizationId, item.OccurredAt });
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
