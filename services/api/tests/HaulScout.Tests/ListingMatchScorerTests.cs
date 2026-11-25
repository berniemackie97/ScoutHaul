using HaulScout.Application.Inventory;

namespace HaulScout.Tests;

public sealed class ListingMatchScorerTests
{
    private static readonly MatchListing Sweater = new("Patagonia Better Sweater 1/4 Zip Fleece Gray Mens L", "Patagonia", "L", "Gray", "Sweaters", 72m, "PM-1");

    private static MatchCandidate Item(
        string title,
        string? brand = "Patagonia",
        string? size = "L",
        string category = "Clothing",
        decimal? price = 64m,
        bool sold = false,
        bool elsewhere = false,
        bool same = false,
        params string[] colors) =>
        new(Guid.NewGuid(), title, brand, category, colors.Length == 0 ? ["Gray"] : colors, size is null ? null : "US letter", size, price, sold, elsewhere, same);

    [Fact]
    public void The_right_item_scores_strong_with_its_reasons()
    {
        ScoredCandidate scored = ListingMatchScorer.Score(Sweater, Item("Patagonia Better Sweater 1/4 Zip Gray"));

        Assert.True(scored.Score >= ListingMatchScorer.Strong, $"scored {scored.Score}");
        Assert.Contains(scored.Signals, signal => signal.Kind == "brand" && signal.Verdict == MatchVerdict.Agrees);
        Assert.Contains(scored.Signals, signal => signal.Kind == "size" && signal.Verdict == MatchVerdict.Agrees);
    }

    [Fact]
    public void A_different_size_or_brand_sinks_an_otherwise_similar_title()
    {
        int right = ListingMatchScorer.Score(Sweater, Item("Patagonia Better Sweater 1/4 Zip Gray")).Score;
        int wrongSize = ListingMatchScorer.Score(Sweater, Item("Patagonia Better Sweater 1/4 Zip Gray", size: "M")).Score;
        int wrongBrand = ListingMatchScorer.Score(Sweater, Item("Better Sweater 1/4 Zip Gray Fleece", brand: "Columbia")).Score;

        Assert.True(wrongSize < ListingMatchScorer.Likely, $"wrong size scored {wrongSize}");
        Assert.True(wrongBrand <= 50, $"wrong brand scored {wrongBrand}");
        Assert.True(right > wrongSize && right > wrongBrand);
    }

    [Fact]
    public void Sold_items_and_items_already_on_this_channel_are_shown_but_never_first()
    {
        MatchCandidate open = Item("Patagonia Better Sweater 1/4 Zip Gray");
        MatchCandidate sold = Item("Patagonia Better Sweater 1/4 Zip Gray Fleece", sold: true);
        MatchCandidate elsewhere = Item("Patagonia Better Sweater 1/4 Zip Gray", elsewhere: true);

        IReadOnlyList<ScoredCandidate> ranked = ListingMatchScorer.Rank(Sweater, [sold, elsewhere, open], floor: 0);

        Assert.Equal(open.Id, ranked[0].Candidate.Id);
        Assert.Contains(ranked, entry => entry.Candidate.Id == sold.Id && entry.Signals.Any(signal => signal.Kind == "sold"));
    }

    [Fact]
    public void The_item_this_listing_was_linked_to_before_is_certain()
    {
        ScoredCandidate scored = ListingMatchScorer.Score(Sweater, Item("Gray fleece", brand: null, size: null, same: true));

        Assert.Equal(100, scored.Score);
    }

    [Fact]
    public void One_shared_word_is_never_a_likely_match()
    {
        MatchListing hoodie = new("Nike Hoodie Olive", null, null, null, null, null, "X");
        int score = ListingMatchScorer.Score(hoodie, Item("Olive planter pot", brand: null, size: null, category: "Home", price: null)).Score;

        Assert.True(score < ListingMatchScorer.Likely, $"scored {score}");
    }

    [Fact]
    public void Brands_written_differently_still_agree()
    {
        MatchListing levis = new("Levis 501 jeans", "Levi Strauss & Co.", null, null, null, null, "X");

        ScoredCandidate scored = ListingMatchScorer.Score(levis, Item("Levi's 501 Jeans", brand: "Levi's", size: null));

        Assert.Contains(scored.Signals, signal => signal.Kind == "brand" && signal.Verdict == MatchVerdict.Agrees);
    }
}
