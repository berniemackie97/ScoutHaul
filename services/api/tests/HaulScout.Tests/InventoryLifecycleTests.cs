using HaulScout.Domain;

namespace HaulScout.Tests;

public class InventoryLifecycleTests
{
    [Theory]
    [InlineData(InventoryStatus.Listed, false)]
    [InlineData(InventoryStatus.Sold, true)]
    [InlineData(InventoryStatus.Packed, true)]
    [InlineData(InventoryStatus.Shipped, true)]
    [InlineData(InventoryStatus.Delivered, true)]
    [InlineData(InventoryStatus.Returned, false)]
    [InlineData(InventoryStatus.Draft, false)]
    public void IsSold_is_true_for_sold_and_every_fulfillment_stage(InventoryStatus status, bool expected)
        => Assert.Equal(expected, InventoryLifecycle.IsSold(status));

    [Fact]
    public void Shipped_ranks_after_Sold_which_ranks_after_Listed()
    {
        Assert.True(InventoryLifecycle.Rank(InventoryStatus.Listed) < InventoryLifecycle.Rank(InventoryStatus.Sold));
        Assert.True(InventoryLifecycle.Rank(InventoryStatus.Sold) < InventoryLifecycle.Rank(InventoryStatus.Shipped));
        Assert.True(InventoryLifecycle.Rank(InventoryStatus.Shipped) < InventoryLifecycle.Rank(InventoryStatus.Delivered));
    }

    [Fact]
    public void Active_group_excludes_sold_and_fulfillment()
    {
        var active = InventoryLifecycle.InGroup(InventoryLifecycleGroup.Active);
        Assert.Contains(InventoryStatus.Listed, active);
        Assert.DoesNotContain(InventoryStatus.Sold, active);
        Assert.DoesNotContain(InventoryStatus.Shipped, active);
        Assert.DoesNotContain(InventoryStatus.Delivered, active);
    }

    [Fact]
    public void Sold_group_includes_all_fulfillment_stages_in_order()
    {
        Assert.Equal(
            new[] { InventoryStatus.Sold, InventoryStatus.Packed, InventoryStatus.Shipped, InventoryStatus.Delivered },
            InventoryLifecycle.InGroup(InventoryLifecycleGroup.Sold));
    }

    [Theory]
    [InlineData(InventoryStatus.Draft, InventoryLifecycleGroup.Active)]
    [InlineData(InventoryStatus.Listed, InventoryLifecycleGroup.Active)]
    [InlineData(InventoryStatus.Shipped, InventoryLifecycleGroup.Sold)]
    [InlineData(InventoryStatus.Returned, InventoryLifecycleGroup.Returned)]
    [InlineData(InventoryStatus.Disposed, InventoryLifecycleGroup.Removed)]
    public void Group_maps_status_to_its_lifecycle_bucket(InventoryStatus status, InventoryLifecycleGroup expected)
        => Assert.Equal(expected, InventoryLifecycle.Group(status));
}
