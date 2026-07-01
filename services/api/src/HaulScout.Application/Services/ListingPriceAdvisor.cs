using System.Globalization;
using System.Text.RegularExpressions;
using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public sealed record ListingPriceAdvice(
    decimal? SuggestedListPrice,
    string? PriceRationale,
    IReadOnlyList<ResaleComp> PricingComps);

/// <summary>
/// Converts returned resale comps into a deterministic marketplace list price. LLMs may help find
/// and summarize comps, but the final price needs to be repeatable, auditable, and protected from
/// common mismatches such as raw-vs-graded, language/region, lots, special variants, and outliers.
/// </summary>
public static class ListingPriceAdvisor
{
    private static readonly Regex SlashIdentifierRegex = new(@"\b\d{1,4}/\d{1,4}\b", RegexOptions.Compiled);
    private static readonly Regex GraderGradeRegex = new(
        @"\b(?<grader>psa|cgc|bgs|sgc)\s*(?<grade>10|9\.5|9|8\.5|8|7\.5|7|6\.5|6|5|4|3|2|1)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] GradedTerms =
    [
        "psa",
        "cgc",
        "bgs",
        "sgc",
        "graded",
        "slab",
        "gem mint",
        "mint 10",
    ];

    private static readonly string[] RawTerms =
    [
        "raw",
        "ungraded",
    ];

    private static readonly string[] LotTerms =
    [
        "lot",
        "bundle",
        "set of",
        "collection",
    ];

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

    public static ListingPriceAdvice Calculate(GenerateListingRequest request, ResaleEstimate? estimate)
    {
        if (estimate is null)
        {
            return new ListingPriceAdvice(null, null, []);
        }

        IReadOnlyList<ResaleComp> allComps = estimate.Comps ?? [];
        ItemMarketProfile itemProfile = ItemMarketProfile.From(request);
        List<CompEvidence> evidence = allComps
            .Select(comp => CompEvidence.Assess(comp, itemProfile))
            .ToList();

        List<ResaleComp> acceptedSold = evidence
            .Where(comp => comp.IsSold && comp.IsCompatible)
            .Select(comp => comp.Comp)
            .ToList();
        List<ResaleComp> soldComps = RemovePriceOutliers(acceptedSold);

        if (soldComps.Count > 0)
        {
            decimal recommendation = RecommendFromSoldComps(soldComps);
            decimal rounded = RoundToMarketplaceEnding(recommendation);
            decimal low = soldComps.Min(comp => comp.Price);
            decimal high = soldComps.Max(comp => comp.Price);
            int rejectedSoldCount = allComps.Count(IsSoldComp) - soldComps.Count;

            string rationale = $"Based on {soldComps.Count} compatible sold comp{Plural(soldComps.Count)}"
                + $" ({Money(low)}–{Money(high)}); priced near the upper-middle of the sold range"
                + (rejectedSoldCount > 0 ? $" after excluding {rejectedSoldCount} mismatched/outlier sold comp{Plural(rejectedSoldCount)}." : ".");

            return new ListingPriceAdvice(rounded, rationale, soldComps.Take(8).ToArray());
        }

        List<ResaleComp> acceptedActive = evidence
            .Where(comp => !comp.IsSold && comp.IsCompatible)
            .Select(comp => comp.Comp)
            .Take(8)
            .ToList();

        decimal? rangePrice = PriceFromRange(estimate);
        if (rangePrice is > 0)
        {
            string rationale = acceptedActive.Count > 0
                ? $"No compatible sold comps were returned; priced from the resale range with {acceptedActive.Count} active comp{Plural(acceptedActive.Count)} as weak support."
                : "No compatible sold comps were returned; priced from the estimated resale range and should be manually verified.";

            return new ListingPriceAdvice(RoundToMarketplaceEnding(rangePrice.Value), rationale, acceptedActive);
        }

        return new ListingPriceAdvice(null, estimate.Rationale, acceptedActive);
    }

    private static bool IsSoldComp(ResaleComp comp) =>
        string.Equals(comp.Status, "sold", StringComparison.OrdinalIgnoreCase);

    private static decimal? PriceFromRange(ResaleEstimate estimate)
    {
        if (estimate.High <= 0)
        {
            return null;
        }

        if (estimate.Low <= 0 || estimate.Low >= estimate.High)
        {
            return estimate.High;
        }

        return string.Equals(estimate.PriceRangeType, "wide", StringComparison.OrdinalIgnoreCase)
            ? estimate.Low + ((estimate.High - estimate.Low) * 0.60m)
            : estimate.High;
    }

    private static decimal RecommendFromSoldComps(IReadOnlyList<ResaleComp> comps)
    {
        List<decimal> prices = comps.Select(comp => comp.Price).OrderBy(price => price).ToList();
        decimal median = Median(prices);
        if (prices.Count < 3)
        {
            return median;
        }

        decimal upperMiddle = PercentileNearestRank(prices, 0.75m);
        decimal guardedUpper = median * 1.12m;
        return Math.Max(median, Math.Min(upperMiddle, guardedUpper));
    }

    private static List<ResaleComp> RemovePriceOutliers(IReadOnlyList<ResaleComp> comps)
    {
        if (comps.Count < 3)
        {
            return comps.ToList();
        }

        List<decimal> ordered = comps.Select(comp => comp.Price).OrderBy(price => price).ToList();
        decimal median = Median(ordered);
        decimal lowerBound = median * (median < 20m ? 0.20m : 0.40m);
        decimal upperBound = median * 2.50m;

        List<ResaleComp> filtered = comps
            .Where(comp => comp.Price >= lowerBound && comp.Price <= upperBound)
            .ToList();

        return filtered.Count > 0 ? filtered : comps.ToList();
    }

    private static decimal Median(IReadOnlyList<decimal> ordered)
    {
        int middle = ordered.Count / 2;
        return ordered.Count % 2 == 0
            ? decimal.Round((ordered[middle - 1] + ordered[middle]) / 2, 2, MidpointRounding.AwayFromZero)
            : ordered[middle];
    }

    private static decimal PercentileNearestRank(IReadOnlyList<decimal> ordered, decimal percentile)
    {
        int rank = Math.Clamp((int)Math.Ceiling(percentile * ordered.Count), 1, ordered.Count);
        return ordered[rank - 1];
    }

    private static decimal RoundToMarketplaceEnding(decimal value)
    {
        decimal rounded = decimal.Round(Math.Max(0, value), 2, MidpointRounding.AwayFromZero);
        if (rounded < 1m)
        {
            return rounded;
        }

        decimal up = Math.Floor(rounded) + 0.99m;
        decimal down = up - 1m;
        if (down < 0.99m)
        {
            return decimal.Round(up, 2, MidpointRounding.AwayFromZero);
        }

        decimal candidate = Math.Abs(up - rounded) <= Math.Abs(rounded - down) ? up : down;
        return decimal.Round(candidate, 2, MidpointRounding.AwayFromZero);
    }

    private static string Money(decimal value) => value.ToString("$0.##", CultureInfo.InvariantCulture);

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private static string NormalizeText(string text) =>
        $" {Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim()} ";

    private static bool ContainsTerm(string normalizedText, string term) =>
        normalizedText.Contains(term.Contains(' ') ? term : $" {term} ", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string normalizedText, IEnumerable<string> terms) =>
        terms.Any(term => ContainsTerm(normalizedText, term));

    private static HashSet<string> ExtractSlashIdentifiers(string text) =>
        SlashIdentifierRegex.Matches(text)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> DetectSpecialVariants(string normalizedText) =>
        SpecialVariantTerms
            .Where(group => ContainsAny(normalizedText, group.Value))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string? DetectLanguage(string normalizedText)
    {
        foreach ((string language, string[] terms) in LanguageTerms)
        {
            if (terms.Any(term => ContainsTerm(normalizedText, term)))
            {
                return language;
            }
        }

        return null;
    }

    private static CertificationProfile DetectCertification(string normalizedText)
    {
        Match match = GraderGradeRegex.Match(normalizedText);
        string? grader = match.Success ? match.Groups["grader"].Value.ToUpperInvariant() : null;
        decimal? grade = match.Success && decimal.TryParse(match.Groups["grade"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : null;

        bool explicitlyRaw = ContainsAny(normalizedText, RawTerms);
        bool graded = match.Success || ContainsAny(normalizedText, GradedTerms);
        return new CertificationProfile(graded && !explicitlyRaw, explicitlyRaw, grader, grade);
    }

    private sealed record ItemMarketProfile(
        string NormalizedText,
        CertificationProfile Certification,
        bool IsLotOrCollection,
        string? Language,
        HashSet<string> SpecialVariantTags,
        HashSet<string> SlashIdentifiers)
    {
        public static ItemMarketProfile From(GenerateListingRequest request)
        {
            string text = NormalizeText(string.Join(' ', new[]
            {
                request.Title,
                request.Brand,
                request.Model,
                request.Category,
                request.Condition,
                request.Color,
                request.SizeSystem,
                request.SizeValue,
                request.AttributesJson,
                request.Notes,
            }.Where(value => !string.IsNullOrWhiteSpace(value))));

            return new ItemMarketProfile(
                text,
                DetectCertification(text),
                ContainsAny(text, LotTerms),
                DetectLanguage(text),
                DetectSpecialVariants(text),
                ExtractSlashIdentifiers(text));
        }
    }

    private sealed record CompMarketProfile(
        CertificationProfile Certification,
        bool IsLotOrCollection,
        string? Language,
        HashSet<string> SpecialVariantTags,
        HashSet<string> SlashIdentifiers)
    {
        public static CompMarketProfile From(ResaleComp comp)
        {
            string text = NormalizeText($"{comp.Condition} {comp.Note} {comp.SourceUrl}");
            return new CompMarketProfile(
                DetectCertification(text),
                ContainsAny(text, LotTerms),
                DetectLanguage(text),
                DetectSpecialVariants(text),
                ExtractSlashIdentifiers(text));
        }
    }

    private sealed record CertificationProfile(bool IsGraded, bool ExplicitlyRaw, string? Grader, decimal? Grade);

    private sealed record CompEvidence(ResaleComp Comp, bool IsSold, bool IsCompatible, IReadOnlyList<string> RejectionReasons)
    {
        public static CompEvidence Assess(ResaleComp comp, ItemMarketProfile item)
        {
            List<string> reasons = [];
            CompMarketProfile candidate = CompMarketProfile.From(comp);

            if (comp.Price <= 0)
            {
                reasons.Add("invalid price");
            }

            EvaluateIdentifiers(item, candidate, reasons);
            EvaluateCertification(item, candidate, reasons);
            EvaluateQuantity(item, candidate, reasons);
            EvaluateLanguage(item, candidate, reasons);
            EvaluateSpecialVariants(item, candidate, reasons);

            return new CompEvidence(comp, IsSoldComp(comp), reasons.Count == 0, reasons);
        }
    }

    private static void EvaluateIdentifiers(ItemMarketProfile item, CompMarketProfile candidate, List<string> reasons)
    {
        if (item.SlashIdentifiers.Count > 0
            && candidate.SlashIdentifiers.Count > 0
            && !candidate.SlashIdentifiers.Overlaps(item.SlashIdentifiers))
        {
            reasons.Add("identifier mismatch");
        }
    }

    private static void EvaluateCertification(ItemMarketProfile item, CompMarketProfile candidate, List<string> reasons)
    {
        if (item.Certification.IsGraded)
        {
            if (!candidate.Certification.IsGraded)
            {
                reasons.Add("raw/ungraded comp for graded item");
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.Certification.Grader)
                && !string.IsNullOrWhiteSpace(candidate.Certification.Grader)
                && !string.Equals(item.Certification.Grader, candidate.Certification.Grader, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("grading-company mismatch");
            }

            if (item.Certification.Grade.HasValue
                && candidate.Certification.Grade.HasValue
                && item.Certification.Grade.Value != candidate.Certification.Grade.Value)
            {
                reasons.Add("grade mismatch");
            }

            return;
        }

        if (candidate.Certification.IsGraded)
        {
            reasons.Add("graded comp for raw/ungraded item");
        }
    }

    private static void EvaluateQuantity(ItemMarketProfile item, CompMarketProfile candidate, List<string> reasons)
    {
        if (item.IsLotOrCollection)
        {
            if (!candidate.IsLotOrCollection)
            {
                reasons.Add("single-item comp for lot/collection");
            }

            return;
        }

        if (candidate.IsLotOrCollection)
        {
            reasons.Add("lot/collection comp for single item");
        }
    }

    private static void EvaluateLanguage(ItemMarketProfile item, CompMarketProfile candidate, List<string> reasons)
    {
        if (!string.IsNullOrWhiteSpace(item.Language))
        {
            if (string.Equals(item.Language, "english", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(candidate.Language)
                    && !string.Equals(candidate.Language, "english", StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add("language mismatch");
                }

                return;
            }

            if (!string.Equals(item.Language, candidate.Language, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("language mismatch");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Language)
            && !string.Equals(candidate.Language, "english", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("foreign-language comp for unspecified-language item");
        }
    }

    private static void EvaluateSpecialVariants(ItemMarketProfile item, CompMarketProfile candidate, List<string> reasons)
    {
        HashSet<string> allTags = item.SpecialVariantTags
            .Concat(candidate.SpecialVariantTags)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string tag in allTags)
        {
            bool itemHasTag = item.SpecialVariantTags.Contains(tag);
            bool candidateHasTag = candidate.SpecialVariantTags.Contains(tag);
            if (itemHasTag != candidateHasTag)
            {
                reasons.Add($"variant mismatch: {tag}");
            }
        }
    }
}
