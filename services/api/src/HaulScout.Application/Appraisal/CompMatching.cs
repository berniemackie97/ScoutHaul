using System.Globalization;
using System.Text.RegularExpressions;

namespace HaulScout.Application.Appraisal;

public enum ConditionBand
{
    Unknown,
    New,
    Used,
    ForParts,
}

/// <summary>What a piece of text says about the market an item belongs to.</summary>
public sealed record MarketProfile(
    bool IsGraded,
    bool IsExplicitlyRaw,
    string? Grader,
    decimal? Grade,
    bool IsLot,
    string? Language,
    IReadOnlySet<string> SpecialVariants,
    IReadOnlySet<string> CardNumbers,
    ConditionBand Condition);

/// <summary>
/// Decides whether a comp describes the same product as the item: same variant, same grading, same
/// quantity, same language, same condition band, and enough of the same words.
/// </summary>
public static class CompMatching
{
    private static readonly Regex CardNumberRegex = new(@"\b\d{1,4}/\d{1,4}\b", RegexOptions.Compiled);
    private static readonly Regex GraderGradeRegex = new(
        @"\b(?<grader>psa|cgc|bgs|sgc)\s*(?<grade>10|9\.5|9|8\.5|8|7\.5|7|6\.5|6|5|4|3|2|1)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TokenRegex = new(@"[a-z0-9]+", RegexOptions.Compiled);

    private static readonly string[] GradedTerms = ["psa", "cgc", "bgs", "sgc", "graded", "slab", "gem mint", "mint 10"];
    private static readonly string[] RawTerms = ["raw", "ungraded"];
    private static readonly string[] LotTerms = ["lot", "bundle", "set of", "collection"];

    private static readonly Dictionary<string, string[]> LanguageTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = ["english", "eng"],
        ["japanese"] = ["japanese", "jpn", "japan"],
        ["korean"] = ["korean", "kor"],
        ["chinese"] = ["chinese"],
        ["spanish"] = ["spanish"],
        ["german"] = ["german"],
        ["french"] = ["french"],
        ["italian"] = ["italian"],
        ["portuguese"] = ["portuguese"],
    };

    private static readonly Dictionary<string, string[]> SpecialVariantTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["proxy/custom"] = ["proxy", "proxies", "orica", "custom", "fan art", "fanart", "replica", "counterfeit", "fake"],
        ["reprint"] = ["reprint"],
        ["gold/metal"] = ["gold foil", "gold card", "metal card"],
        ["digital/code"] = ["digital", "code card"],
        ["oversized"] = ["oversized", "jumbo"],
    };

    private static readonly string[] ForPartsTerms =
        ["for parts", "parts only", "not working", "non working", "nonworking", "broken", "as is", "defective"];
    private static readonly string[] UsedBeforeNewTerms = ["like new", "open box"];
    private static readonly string[] NewTerms =
        ["new", "nwt", "nwot", "nib", "bnib", "bnwt", "sealed", "deadstock", "brand new"];
    private static readonly string[] UsedTerms =
    [
        "used", "pre owned", "preowned", "good", "very good", "excellent", "fair", "acceptable", "gently",
        "worn", "euc", "guc", "vguc", "loose", "complete in box", "cib", "refurbished", "poor",
    ];

    // Words a listing title uses that say nothing about which product it is.
    private static readonly HashSet<string> FillerWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "of", "for", "with", "in", "on", "by", "to", "from", "w",
        "new", "used", "nwt", "nwot", "nib", "euc", "guc", "vguc", "preowned", "pre", "owned", "condition",
        "great", "good", "excellent", "nice", "free", "shipping", "ship", "fast", "authentic", "genuine",
        "rare", "htf", "item", "tested", "works", "working", "oem", "original", "brand",
    };

    public static MarketProfile Profile(string? condition, params string?[] text)
    {
        string normalized = Normalize(string.Join(' ', text.Append(condition).Where(part => !string.IsNullOrWhiteSpace(part))));
        Match grading = GraderGradeRegex.Match(normalized);
        bool explicitlyRaw = ContainsAny(normalized, RawTerms);
        bool graded = (grading.Success || ContainsAny(normalized, GradedTerms)) && !explicitlyRaw;
        decimal? grade = grading.Success
            && decimal.TryParse(grading.Groups["grade"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
                ? parsed
                : null;

        return new MarketProfile(
            IsGraded: graded,
            IsExplicitlyRaw: explicitlyRaw,
            Grader: grading.Success ? grading.Groups["grader"].Value.ToUpperInvariant() : null,
            Grade: grade,
            IsLot: ContainsAny(normalized, LotTerms),
            Language: DetectLanguage(normalized),
            SpecialVariants: SpecialVariantTerms
                .Where(group => ContainsAny(normalized, group.Value))
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            CardNumbers: CardNumberRegex.Matches(normalized)
                .Select(match => match.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            Condition: BandOf(condition));
    }

    /// <summary>Why the comp is a different product from the item; empty when it is the same one.</summary>
    public static IReadOnlyList<string> Mismatches(MarketProfile item, MarketProfile comp)
    {
        List<string> reasons = [];

        if (item.CardNumbers.Count > 0 && comp.CardNumbers.Count > 0 && !comp.CardNumbers.Overlaps(item.CardNumbers))
        {
            reasons.Add("identifier mismatch");
        }

        if (item.IsGraded)
        {
            if (!comp.IsGraded)
            {
                reasons.Add("raw/ungraded comp for graded item");
            }
            else
            {
                if (item.Grader is not null && comp.Grader is not null
                    && !string.Equals(item.Grader, comp.Grader, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add("grading-company mismatch");
                }

                if (item.Grade.HasValue && comp.Grade.HasValue && item.Grade.Value != comp.Grade.Value)
                {
                    reasons.Add("grade mismatch");
                }
            }
        }
        else if (comp.IsGraded)
        {
            reasons.Add("graded comp for raw/ungraded item");
        }

        if (item.IsLot != comp.IsLot)
        {
            reasons.Add(item.IsLot ? "single-item comp for lot/collection" : "lot/collection comp for single item");
        }

        string? languageMismatch = LanguageMismatch(item.Language, comp.Language);
        if (languageMismatch is not null)
        {
            reasons.Add(languageMismatch);
        }

        foreach (string tag in item.SpecialVariants.Concat(comp.SpecialVariants).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (item.SpecialVariants.Contains(tag) != comp.SpecialVariants.Contains(tag))
            {
                reasons.Add($"variant mismatch: {tag}");
            }
        }

        return reasons;
    }

    /// <summary>A comp in a different condition band prices a different market, except when either is unknown.</summary>
    public static string? ConditionMismatch(ConditionBand item, ConditionBand comp)
    {
        if (item == ConditionBand.Unknown || comp == ConditionBand.Unknown || item == comp)
        {
            return null;
        }

        return comp switch
        {
            ConditionBand.ForParts => "sold for parts",
            ConditionBand.New => "new condition",
            _ => item == ConditionBand.ForParts ? "working condition" : "used condition",
        };
    }

    public static ConditionBand BandOf(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return ConditionBand.Unknown;
        }

        string text = Normalize(condition.Replace('-', ' '));
        if (ContainsAny(text, ForPartsTerms)) return ConditionBand.ForParts;
        if (ContainsAny(text, UsedBeforeNewTerms)) return ConditionBand.Used;
        if (ContainsAny(text, NewTerms)) return ConditionBand.New;
        return ContainsAny(text, UsedTerms) ? ConditionBand.Used : ConditionBand.Unknown;
    }

    /// <summary>The words that name a product, lowercased and de-duplicated, filler removed.</summary>
    public static IReadOnlyList<string> Tokens(params string?[] text) =>
        TokenRegex.Matches(string.Join(' ', text.Where(part => !string.IsNullOrWhiteSpace(part))).ToLowerInvariant())
            .Select(match => match.Value)
            .Where(token => (token.Length > 1 || char.IsDigit(token[0])) && !FillerWords.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Share of the item's words the comp also uses, 0 to 1.</summary>
    public static double Coverage(IReadOnlyCollection<string> itemTokens, IReadOnlyCollection<string> compTokens)
    {
        if (itemTokens.Count == 0)
        {
            return 0;
        }

        HashSet<string> comp = compTokens as HashSet<string> ?? compTokens.ToHashSet(StringComparer.Ordinal);
        return (double)itemTokens.Count(token => Shares(token, comp)) / itemTokens.Count;
    }

    /// <summary>
    /// Whether a comp's words describe the item. A short name must match whole. A longer one needs two
    /// shared words and either half of the item's name or most of the comp's, so a terse comp title
    /// ("Air Jordan 1 Lost and Found") still matches a long item name, and "Nike Air Max" never does.
    /// </summary>
    public static bool NamesSameItem(IReadOnlyCollection<string> itemTokens, IReadOnlyCollection<string> compTokens)
    {
        if (itemTokens.Count == 0)
        {
            return false;
        }

        HashSet<string> comp = compTokens as HashSet<string> ?? compTokens.ToHashSet(StringComparer.Ordinal);
        int shared = itemTokens.Count(token => Shares(token, comp));
        if (itemTokens.Count <= 2)
        {
            return shared == itemTokens.Count;
        }

        return shared >= 2
            && ((double)shared / itemTokens.Count >= 0.5 || (comp.Count >= 3 && (double)shared / comp.Count >= 0.6));
    }

    /// <summary>
    /// Whether the comp names a different model or year: both carry identifier words (letters with
    /// digits, or three or more digits) and none of them agree. "WH1000XM5" agrees with "1000XM5";
    /// "1000XM4" does not.
    /// </summary>
    public static bool ModelConflict(IReadOnlyCollection<string> itemTokens, IReadOnlyCollection<string> compTokens)
    {
        string[] item = itemTokens.Where(IsIdentifier).ToArray();
        string[] comp = compTokens.Where(IsIdentifier).ToArray();
        if (item.Length == 0 || comp.Length == 0)
        {
            return false;
        }

        return !item.Any(mine => comp.Any(theirs => SameIdentifier(mine, theirs)));
    }

    // Model numbers are written with and without their hyphens, so "1000xm5" is in "wh1000xm5".
    private static bool Shares(string token, HashSet<string> comp) =>
        comp.Contains(token) || (IsIdentifier(token) && comp.Any(other => IsIdentifier(other) && SameIdentifier(token, other)));

    private static bool SameIdentifier(string a, string b) =>
        a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);

    private static bool IsIdentifier(string token) => token.Length >= 3 && token.Any(char.IsDigit);

    private static string? LanguageMismatch(string? item, string? comp)
    {
        bool compForeign = comp is not null && !string.Equals(comp, "english", StringComparison.OrdinalIgnoreCase);

        if (item is null)
        {
            return compForeign ? "foreign-language comp for unspecified-language item" : null;
        }

        if (string.Equals(item, "english", StringComparison.OrdinalIgnoreCase))
        {
            return compForeign ? "language mismatch" : null;
        }

        return string.Equals(item, comp, StringComparison.OrdinalIgnoreCase) ? null : "language mismatch";
    }

    private static string? DetectLanguage(string normalized)
    {
        foreach ((string language, string[] terms) in LanguageTerms)
        {
            if (ContainsAny(normalized, terms))
            {
                return language;
            }
        }

        return null;
    }

    private static string Normalize(string text) => $" {Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim()} ";

    private static bool ContainsAny(string normalized, IEnumerable<string> terms) =>
        terms.Any(term => normalized.Contains(term.Contains(' ') ? term : $" {term} ", StringComparison.OrdinalIgnoreCase));
}
