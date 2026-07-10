using HaulScout.Domain;

namespace HaulScout.Tests;

public class TitleMatchTests
{
    [Fact]
    public void Tokenize_lowercases_splits_and_drops_filler()
    {
        IReadOnlyList<string> tokens = TitleMatch.Tokenize("Vintage Levi's 501 Denim Jacket - Men's M (USA)");
        Assert.Contains("levi", tokens);
        Assert.Contains("501", tokens);
        Assert.Contains("denim", tokens);
        Assert.Contains("jacket", tokens);
        Assert.DoesNotContain("vintage", tokens); // stopword
        Assert.DoesNotContain("usa", tokens);      // stopword
    }

    [Fact]
    public void Score_is_high_when_the_short_title_is_contained_in_the_long_one()
    {
        double score = TitleMatch.Score("Levi's 501 Denim Jacket", "Vintage Levi's 501 Denim Jacket Men's Medium Distressed USA");
        Assert.True(score >= 0.9, $"expected strong containment, got {score}");
    }

    [Fact]
    public void Score_is_low_for_unrelated_titles()
    {
        Assert.True(TitleMatch.Score("Sony PlayStation 5 Console", "Levi's 501 Denim Jacket") < 0.3);
    }

    [Fact]
    public void Best_picks_the_right_candidate_and_ignores_weak_single_token_overlaps()
    {
        (Guid, string)[] candidates =
        [
            (Guid.NewGuid(), "Nike Air Max 90"),
            (Guid.NewGuid(), "Levi's 501 Denim Jacket"),
            (Guid.NewGuid(), "Denim Bag") // shares only "denim" — must not win
        ];
        Guid expected = candidates[1].Item1;

        Guid? best = TitleMatch.Best("Vintage Levi's 501 Denim Jacket Men's M", candidates);
        Assert.Equal(expected, best);
    }

    [Fact]
    public void Best_returns_null_when_nothing_clears_the_bar()
    {
        (Guid, string)[] candidates = [(Guid.NewGuid(), "Nike Air Max 90"), (Guid.NewGuid(), "Sony Camera")];
        Assert.Null(TitleMatch.Best("Levi's 501 Denim Jacket", candidates));
    }
}
