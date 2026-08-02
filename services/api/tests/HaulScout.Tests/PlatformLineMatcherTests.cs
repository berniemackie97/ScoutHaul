using HaulScout.Domain;

namespace HaulScout.Tests;

public class PlatformLineMatcherTests
{
    private static readonly Guid Boots = Guid.NewGuid();
    private static readonly Guid Jacket = Guid.NewGuid();
    private static readonly Guid SoldHat = Guid.NewGuid();

    private static List<InventoryMatchCandidate> Candidates(params InventoryMatchCandidate[] extra) =>
    [
        new(Boots, "Leather Boots Size 9", "SKU-BOOT-9", AlreadySold: false),
        new(Jacket, "Vintage Denim Jacket", "SKU-JKT-1", AlreadySold: false),
        new(SoldHat, "Wool Felt Hat", "SKU-HAT-3", AlreadySold: true),
        .. extra,
    ];

    private static Dictionary<string, Guid> ByItemId(params (string ItemId, Guid Item)[] pairs) =>
        pairs.ToDictionary(pair => pair.ItemId, pair => pair.Item);

    // ── The chain, strongest signal first ─────────────────────────────

    [Fact]
    public void Platform_item_id_wins_over_everything_else()
    {
        // Title points at the jacket, but the platform's own item number says boots.
        PlatformLineMatch? match = PlatformLineMatcher.Match(
            new PlatformLineIdentity("EBAY-1", "SKU-JKT-1", "Vintage Denim Jacket"),
            ByItemId(("EBAY-1", Boots)),
            Candidates());

        Assert.NotNull(match);
        Assert.Equal(Boots, match.InventoryItemId);
        Assert.Equal(PlatformLineMatchSource.PlatformItemId, match.Source);
    }

    [Fact]
    public void Sku_is_used_when_there_is_no_platform_item_id_match()
    {
        PlatformLineMatch? match = PlatformLineMatcher.Match(
            new PlatformLineIdentity("EBAY-UNKNOWN", "SKU-JKT-1", "something else entirely"),
            ByItemId(),
            Candidates());

        Assert.NotNull(match);
        Assert.Equal(Jacket, match.InventoryItemId);
        Assert.Equal(PlatformLineMatchSource.Sku, match.Source);
    }

    [Fact]
    public void Sku_matching_is_case_insensitive()
        => Assert.Equal(
            Jacket,
            PlatformLineMatcher.Match(
                new PlatformLineIdentity(null, "sku-jkt-1", "irrelevant"), ByItemId(), Candidates())?.InventoryItemId);

    [Fact]
    public void A_duplicated_sku_is_a_data_problem_not_a_match()
    {
        // Two items share a SKU: matching either would be a coin flip, so fall through.
        List<InventoryMatchCandidate> candidates = Candidates(
            new InventoryMatchCandidate(Guid.NewGuid(), "Another Denim Jacket", "SKU-JKT-1", AlreadySold: false));

        PlatformLineMatch? match = PlatformLineMatcher.Match(
            new PlatformLineIdentity(null, "SKU-JKT-1", "nothing resembling a title"), ByItemId(), candidates);

        Assert.True(match is null || match.Source == PlatformLineMatchSource.Title);
    }

    [Fact]
    public void Title_is_the_last_resort()
    {
        PlatformLineMatch? match = PlatformLineMatcher.Match(
            new PlatformLineIdentity(null, null, "Leather Boots Size 9"), ByItemId(), Candidates());

        Assert.NotNull(match);
        Assert.Equal(Boots, match.InventoryItemId);
        Assert.Equal(PlatformLineMatchSource.Title, match.Source);
    }

    [Fact]
    public void Nothing_recognisable_produces_no_match()
        => Assert.Null(PlatformLineMatcher.Match(
            new PlatformLineIdentity(null, null, "zzzz qqqq xxxx"), ByItemId(), Candidates()));

    [Fact]
    public void An_identifier_pointing_outside_the_candidate_set_is_not_offered()
    {
        // e.g. the listing links to an archived item — offering it would be a dead end.
        PlatformLineMatch? match = PlatformLineMatcher.Match(
            new PlatformLineIdentity("EBAY-GONE", null, "zzzz qqqq xxxx"),
            ByItemId(("EBAY-GONE", Guid.NewGuid())),
            Candidates());

        Assert.Null(match);
    }

    // ── The safety property: fuzzy matches never settle themselves ────

    [Fact]
    public void Only_identifier_matches_are_exact()
    {
        Assert.True(PlatformLineMatcher.Match(
            new PlatformLineIdentity("EBAY-1", null, "x"), ByItemId(("EBAY-1", Boots)), Candidates())!.IsExact);
        Assert.True(PlatformLineMatcher.Match(
            new PlatformLineIdentity(null, "SKU-BOOT-9", "x"), ByItemId(), Candidates())!.IsExact);
        Assert.False(PlatformLineMatcher.Match(
            new PlatformLineIdentity(null, null, "Leather Boots Size 9"), ByItemId(), Candidates())!.IsExact);
    }

    [Fact]
    public void A_title_guess_never_auto_settles_because_wrong_money_beats_a_queue_entry()
        => Assert.False(PlatformLineMatcher.Match(
            new PlatformLineIdentity(null, null, "Leather Boots Size 9"), ByItemId(), Candidates())!.CanAutoSettle);

    [Fact]
    public void An_exact_match_on_an_unsold_item_can_settle_itself()
        => Assert.True(PlatformLineMatcher.Match(
            new PlatformLineIdentity("EBAY-1", null, "x"), ByItemId(("EBAY-1", Boots)), Candidates())!.CanAutoSettle);

    [Fact]
    public void An_exact_match_on_an_already_sold_item_still_needs_a_person()
    {
        // Usually a duplicate import or a relisted item — a human has to decide.
        PlatformLineMatch? match = PlatformLineMatcher.Match(
            new PlatformLineIdentity("EBAY-HAT", null, "x"), ByItemId(("EBAY-HAT", SoldHat)), Candidates());

        Assert.NotNull(match);
        Assert.True(match.IsExact);
        Assert.True(match.AlreadySold);
        Assert.False(match.CanAutoSettle);
    }
}
