using HaulScout.Application.Inventory;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

/// <summary>
/// When an item may be archived and when it may actually be destroyed — as pure rules, with no database.
///
/// These guards used to be six EF queries interleaved with the messages they produce, so the only way to ask "what
/// happens if an item has sale history but no live sale?" was to build that world in a database. Now the questions and
/// the rules are separate, and the rule that matters most — history is never silently erased — is checkable directly.
/// </summary>
public class InventoryDeletionRulesTests
{
    private static readonly InventoryReferences Nothing = new(false, false, false, false, false, false, false);

    private static InventoryItem Item(InventoryStatus status = InventoryStatus.ReadyToList, bool isBundle = false) =>
        new() { OrganizationId = Guid.NewGuid(), Title = "Brass Lamp", Status = status, IsBundle = isBundle };

    [Fact]
    public void An_unencumbered_item_can_be_removed()
    {
        Assert.Null(InventoryDeletionRules.BlockedFromRemoval(Item(), Nothing));
        Assert.Null(InventoryDeletionRules.BlockedFromHardDelete(Nothing));
    }

    [Fact]
    public void A_listed_item_cannot_be_removed_until_it_is_delisted()
    {
        string? blocked = InventoryDeletionRules.BlockedFromRemoval(Item(InventoryStatus.Listed), Nothing);

        Assert.Contains("Delist it from every channel", blocked);
    }

    [Fact]
    public void An_item_held_by_a_live_sale_cannot_be_removed()
    {
        InventoryReferences references = Nothing with { HasActiveSale = true };

        Assert.Contains("has an active sale", InventoryDeletionRules.BlockedFromRemoval(Item(), references));
    }

    /// <summary>
    /// The distinction the whole design rests on: a settled sale is history. It must not block archiving — the operator
    /// has to be able to tidy up — but it must block deletion, because erasing the row would rewrite the books.
    /// </summary>
    [Fact]
    public void Sale_history_permits_archiving_but_refuses_deletion()
    {
        InventoryReferences references = Nothing with { HasSaleHistory = true };

        Assert.Null(InventoryDeletionRules.BlockedFromRemoval(Item(), references));
        Assert.Contains("Archive it instead", InventoryDeletionRules.BlockedFromHardDelete(references));
    }

    /// <summary>Same rule, from the customer's side rather than the ledger's.</summary>
    [Fact]
    public void Order_history_permits_archiving_but_refuses_deletion()
    {
        InventoryReferences references = Nothing with { HasOrderHistory = true };

        Assert.Null(InventoryDeletionRules.BlockedFromRemoval(Item(), references));
        Assert.Contains("customer/order history remains intact", InventoryDeletionRules.BlockedFromHardDelete(references));
    }

    [Theory]
    [InlineData(nameof(InventoryReferences.HasLiveChannelListing))]
    [InlineData(nameof(InventoryReferences.HasActiveMarketplaceListing))]
    [InlineData(nameof(InventoryReferences.HasActiveStorefrontListing))]
    public void Any_live_listing_anywhere_blocks_removal(string reference)
    {
        InventoryReferences references = reference switch
        {
            nameof(InventoryReferences.HasLiveChannelListing) => Nothing with { HasLiveChannelListing = true },
            nameof(InventoryReferences.HasActiveMarketplaceListing) => Nothing with { HasActiveMarketplaceListing = true },
            _ => Nothing with { HasActiveStorefrontListing = true },
        };

        Assert.NotNull(InventoryDeletionRules.BlockedFromRemoval(Item(), references));
    }

    [Fact]
    public void An_item_inside_someone_elses_bundle_cannot_be_removed_or_deleted()
    {
        InventoryReferences references = Nothing with { IsBundleComponent = true };

        Assert.Contains("inside a bundle", InventoryDeletionRules.BlockedFromRemoval(Item(), references));
        Assert.Contains("inside a bundle", InventoryDeletionRules.BlockedFromHardDelete(references));
    }

    /// <summary>When a bundle goes, its components must not be stranded in a stage whose bundle no longer exists.</summary>
    [Fact]
    public void Releasing_a_bundle_returns_its_bundled_components_to_ready_to_list()
    {
        InventoryItem component = Item(InventoryStatus.Bundled);
        InventoryItem untouched = Item(InventoryStatus.Sold);
        InventoryItem bundle = Item(isBundle: true);
        bundle.BundleComponents =
        [
            new InventoryBundleComponent { ComponentInventoryItem = component, TitleSnapshot = component.Title },
            new InventoryBundleComponent { ComponentInventoryItem = untouched, TitleSnapshot = untouched.Title },
        ];

        InventoryDeletionRules.ReleaseBundleComponents(bundle);

        Assert.Equal(InventoryStatus.ReadyToList, component.Status);
        Assert.Equal(InventoryStatus.Sold, untouched.Status);   // only Bundled is released; anything else is its own truth
    }

    [Fact]
    public void Releasing_a_non_bundle_does_nothing()
    {
        InventoryItem plain = Item(InventoryStatus.Bundled);

        InventoryDeletionRules.ReleaseBundleComponents(plain);

        Assert.Equal(InventoryStatus.Bundled, plain.Status);
    }
}
