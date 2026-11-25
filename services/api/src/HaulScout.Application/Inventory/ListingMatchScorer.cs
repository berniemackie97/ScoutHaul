using HaulScout.Domain;

namespace HaulScout.Application.Inventory;

/// <summary>What a marketplace listing says, in the shape the scorer reads.</summary>
/// <param name="IsLive">On sale now. A live listing for a sold item is a mistake to flag; an ended or sold one is history.</param>
public sealed record MatchListing(string Title, string? Brand, string? Size, string? Color, string? Category, decimal? Price, string? ExternalListingId, bool IsLive = true);

/// <summary>An inventory item that might be the listing's.</summary>
public sealed record MatchCandidate(
    Guid Id,
    string Title,
    string? Brand,
    string? Category,
    IReadOnlyList<string> Colors,
    string? SizeSystem,
    string? SizeValue,
    decimal? Price,
    bool IsSold,
    bool ListedOnThisChannel,
    bool SameListing);

public enum MatchVerdict
{
    Agrees,
    Partial,
    Disagrees,
}

/// <summary>One reason a candidate scored as it did, shown beside it.</summary>
public sealed record MatchSignal(string Kind, MatchVerdict Verdict, string Text);

/// <param name="Demoted">Sold while the listing is live, or already on this channel: shown, never ranked above a clean candidate.</param>
public sealed record ScoredCandidate(MatchCandidate Candidate, int Score, IReadOnlyList<MatchSignal> Signals, bool Demoted = false);

/// <summary>
/// Scores how likely an inventory item is the one a marketplace listing is for, 0–100, with the reasons. Title words
/// carry most of it; brand, size, kind of item, color and price confirm or contradict. A contradiction weighs more than
/// agreement — a different size or brand is strong evidence of a different item — and a sold item or one already on
/// this channel under another listing is shown but ranked after every clean one. Deterministic and explainable: a suggestion a
/// person confirms, never an automatic link (except the listing seen on this item before).
/// </summary>
public static class ListingMatchScorer
{
    public const int Strong = 90;
    public const int Likely = 70;

    private const double TitleWeight = 40;
    private const double BrandWeight = 15;
    private const double SizeWeight = 15;
    private const double KindWeight = 10;
    private const double ColorWeight = 5;
    private const double PriceWeight = 10;

    public static IReadOnlyList<ScoredCandidate> Rank(MatchListing listing, IEnumerable<MatchCandidate> candidates, int take = 6, int floor = 25) =>
        [
            .. candidates
                .Select(candidate => Score(listing, candidate))
                .Where(scored => scored.Score >= floor || scored.Candidate.SameListing)
                .OrderBy(scored => scored.Demoted)
                .ThenByDescending(scored => scored.Score)
                .Take(take),
        ];

    public static ScoredCandidate Score(MatchListing listing, MatchCandidate candidate)
    {
        List<MatchSignal> signals = [];
        if (candidate.SameListing)
        {
            signals.Add(new("listing", MatchVerdict.Agrees, "linked to this listing before"));
            return new ScoredCandidate(candidate, 100, signals);
        }

        // Scored against what both sides actually say: an item with only a title is not marked down for the brand it lacks.
        double points = 0;
        double possible = TitleWeight;
        int cap = 100;

        (double title, int shared) = TitleSimilarity(listing.Title, candidate.Title);
        points += TitleWeight * title;
        signals.Add(new("title", title >= 0.75 ? MatchVerdict.Agrees : title >= 0.45 ? MatchVerdict.Partial : MatchVerdict.Disagrees, $"title {Math.Round(title * 100)}%"));
        if (shared < 2)
        {
            cap = Math.Min(cap, 60);
        }

        string? brand = Clean(listing.Brand);
        string? candidateBrand = Clean(candidate.Brand);
        if (brand is not null && candidateBrand is not null)
        {
            possible += BrandWeight;
            if (SameBrand(brand, candidateBrand))
            {
                points += BrandWeight;
                signals.Add(new("brand", MatchVerdict.Agrees, "brand"));
            }
            else
            {
                cap = Math.Min(cap, 50);
                signals.Add(new("brand", MatchVerdict.Disagrees, $"brand {listing.Brand!.Trim()}"));
            }
        }

        ItemProfile profile = ItemProfiles.For(candidate.Category);
        if (!string.IsNullOrWhiteSpace(candidate.SizeValue) && ItemSizes.Read(listing.Size, profile) is { } size)
        {
            possible += SizeWeight;
            if (string.Equals(size.Value, candidate.SizeValue, StringComparison.OrdinalIgnoreCase))
            {
                points += SizeWeight;
                signals.Add(new("size", MatchVerdict.Agrees, $"size {size.Value}"));
            }
            else
            {
                points -= SizeWeight * 1.5;
                signals.Add(new("size", MatchVerdict.Disagrees, $"size {size.Value}"));
            }
        }

        string? category = ItemCategories.Canonical(ItemCategories.Standard, listing.Category);
        if (category is not null && !string.IsNullOrWhiteSpace(candidate.Category))
        {
            ItemProfile listingProfile = ItemProfiles.For(category);
            if (string.Equals(category, candidate.Category, StringComparison.OrdinalIgnoreCase) || listingProfile.Key == profile.Key)
            {
                possible += KindWeight;
                points += KindWeight;
                signals.Add(new("kind", MatchVerdict.Agrees, profile.Label));
            }
            else if (listingProfile.Key != ItemProfiles.General.Key && profile.Key != ItemProfiles.General.Key)
            {
                possible += KindWeight;
                points -= KindWeight * 1.5;
                signals.Add(new("kind", MatchVerdict.Disagrees, $"{listingProfile.Label} ≠ {profile.Label}"));
            }
        }

        IReadOnlyList<string> colors = ItemColors.Standard.CanonicalList([listing.Color], ItemColors.Max);
        if (colors.Count > 0 && candidate.Colors.Count > 0)
        {
            possible += ColorWeight;
            if (colors.Any(color => candidate.Colors.Contains(color, StringComparer.OrdinalIgnoreCase)))
            {
                points += ColorWeight;
                signals.Add(new("color", MatchVerdict.Agrees, string.Join("/", colors)));
            }
            else
            {
                points -= ColorWeight;
                signals.Add(new("color", MatchVerdict.Disagrees, string.Join("/", colors)));
            }
        }

        if (listing.Price is > 0 && candidate.Price is > 0)
        {
            possible += PriceWeight;
            decimal ratio = Math.Abs(listing.Price.Value - candidate.Price.Value) / candidate.Price.Value;
            if (ratio <= 0.25m)
            {
                points += PriceWeight;
                signals.Add(new("price", MatchVerdict.Agrees, $"price {listing.Price:0.##}"));
            }
            else
            {
                signals.Add(new("price", MatchVerdict.Partial, $"price {listing.Price:0.##} vs {candidate.Price:0.##}"));
            }
        }

        // A title alone, however close, is not certainty.
        if (possible <= TitleWeight)
        {
            cap = Math.Min(cap, 85);
        }

        int score = (int)Math.Round(Math.Clamp(points, 0, possible) / possible * 100);
        score = Math.Min(score, cap);
        bool demoted = false;
        if (candidate.IsSold && listing.IsLive)
        {
            demoted = true;
            signals.Add(new("sold", MatchVerdict.Disagrees, "already sold"));
        }
        else if (candidate.IsSold)
        {
            signals.Add(new("sold", MatchVerdict.Partial, "sold"));
        }
        else if (candidate.ListedOnThisChannel)
        {
            demoted = true;
            signals.Add(new("channel", MatchVerdict.Disagrees, "already listed here"));
        }

        return new ScoredCandidate(candidate, score, signals, demoted);
    }

    /// <summary>
    /// Title words in common, read both ways: containment (a short inventory title inside a long, keyword-stuffed
    /// listing title) blended with Dice (both describe the same thing), so neither a one-word title nor a padded one wins.
    /// </summary>
    public static (double Similarity, int Shared) TitleSimilarity(string? a, string? b)
    {
        HashSet<string> left = Words(a);
        HashSet<string> right = Words(b);
        if (left.Count == 0 || right.Count == 0)
        {
            return (0, 0);
        }

        int shared = left.Count(right.Contains);
        double containment = (double)shared / Math.Min(left.Count, right.Count);
        double dice = 2.0 * shared / (left.Count + right.Count);
        return (0.6 * containment + 0.4 * dice, shared);
    }

    private static HashSet<string> Words(string? title) =>
        TitleMatch.Tokenize(title).Select(Stem).ToHashSet(StringComparer.Ordinal);

    // Plural and possessive endings only: "jeans"/"jean", "levis"/"levi".
    private static string Stem(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1] : word;

    private static string? Clean(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand))
        {
            return null;
        }

        string letters = new([.. brand.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
        return letters.Length == 0 || letters is "unbranded" or "none" or "other" or "na" ? null : letters;
    }

    // "Levi's" and "Levi Strauss & Co." are one brand: same name, or one starting with the other's first word.
    private static bool SameBrand(string a, string b) =>
        a == b || (a.Length >= 4 && b.StartsWith(a, StringComparison.Ordinal)) || (b.Length >= 4 && a.StartsWith(b, StringComparison.Ordinal))
        || (a.Length >= 4 && b.Length >= 4 && a[..4] == b[..4]);
}
