using System.Globalization;

namespace HaulScout.Application.Appraisal;

/// <summary>
/// Turns comps into a price and a confidence. Deterministic: the same comps always give the same
/// answer, and every point of confidence carries the reason it was given or taken away.
/// </summary>
public static class CompPricing
{
    private const double SaleHalfLifeDays = 180;
    private const double MinimumRecencyWeight = 0.25;
    private const double UndatedWeight = 0.5;
    private const double UnverifiedWeight = 0.6;
    private const double YourSaleWeight = 1.25;
    private static readonly double OutlierFloorBand = Math.Log(1.5);
    private static readonly double SmallPoolBand = Math.Log(2.5);

    public static PriceResult Price(ItemFacts item, IReadOnlyList<Comp> comps, ModelEstimate? estimate, DateTimeOffset now)
    {
        MarketProfile itemProfile = CompMatching.Profile(
            item.Condition,
            [item.Title, item.Brand, item.Model, item.Category, item.Color, .. item.Identifiers]);
        IReadOnlyList<string> itemTokens = CompMatching.Tokens(item.Title, item.Brand, item.Model);

        List<AssessedComp> assessed = comps.Select(comp => Assess(comp, itemProfile, itemTokens)).ToList();
        assessed = TrimOutliers(assessed, IsSale);
        assessed = TrimOutliers(assessed, comp => comp.Kind == CompKind.Asking);

        List<Comp> sales = Included(assessed, IsSale);
        List<Comp> guides = Included(assessed, comp => comp.Kind == CompKind.Guide);
        List<Comp> asking = Included(assessed, comp => comp.Kind == CompKind.Asking);

        List<string> reasons = [];
        Pricing pricing = sales.Count > 0 ? FromSales(sales, itemProfile.Condition, now, reasons)
            : guides.Count > 0 ? FromGuide(guides, reasons)
            : asking.Count > 0 ? FromAsking(asking, reasons)
            : estimate is { High: > 0 } ? FromEstimate(estimate, reasons)
            : NoPrice(reasons);

        if (sales.Count > 0 && asking.Count > 0)
        {
            reasons.Add($"{Count(asking.Count, "current listing")} from {Money(asking.Min(comp => comp.Price))}.");
        }

        int score = pricing.Score;
        if (pricing.Basis != PriceBasis.None)
        {
            score -= IdentificationPenalty(item.Certainty, reasons);
        }

        AddExclusionSummary(assessed, reasons);
        score = Math.Clamp(score, 0, pricing.Cap);

        return new PriceResult(
            pricing.Basis,
            Round(pricing.QuickSale),
            Round(pricing.List),
            Round(pricing.High),
            score,
            LevelFor(score, pricing.Basis),
            reasons,
            Order(assessed));
    }

    private sealed record Pricing(PriceBasis Basis, decimal? QuickSale, decimal? List, decimal? High, int Score, int Cap);

    private static Pricing FromSales(List<Comp> sales, ConditionBand itemCondition, DateTimeOffset now, List<string> reasons)
    {
        List<(decimal Value, double Weight)> points = sales.Select(sale => (sale.Price, SaleWeight(sale, now))).ToList();
        decimal list = sales.Count == 1 ? sales[0].Price : PriceMath.WeightedPercentile(points, 0.65);
        decimal quick = sales.Count == 1 ? list * 0.85m : PriceMath.WeightedPercentile(points, 0.25);
        decimal high = sales.Count == 1 ? list : PriceMath.WeightedPercentile(points, 0.90);

        int yours = sales.Count(sale => sale.Kind == CompKind.YourSale);
        string whose = yours == 0 ? string.Empty
            : yours < sales.Count ? $", {yours} of them yours"
            : sales.Count == 1 ? ", yours"
            : ", all yours";
        reasons.Add($"{Count(sales.Count, "matching sale")}{whose}.");

        double totalWeight = points.Sum(point => point.Weight);
        int score = (int)Math.Min(40, Math.Round(12 * totalWeight));
        score += SpreadPoints(sales, "Sales", 1, reasons);
        score += RecencyPoints(sales, now, reasons);

        double verifiedWeight = sales.Zip(points).Where(pair => pair.First.Verified).Sum(pair => pair.Second.Weight);
        score += (int)Math.Round(10 * verifiedWeight / totalWeight);
        int unverified = sales.Count(sale => !sale.Verified);
        if (unverified > 0)
        {
            reasons.Add($"{unverified} of {sales.Count} found by web search and not checked against the listing.");
        }

        if (yours > 0)
        {
            score += 5;
        }

        score += ConditionPoints(itemCondition, sales, reasons);
        return new Pricing(PriceBasis.Sales, Math.Min(quick, list), list, Math.Max(high, list), score, 95);
    }

    private static Pricing FromGuide(List<Comp> guides, List<string> reasons)
    {
        Comp cheapest = guides.MinBy(guide => guide.Price)!;
        decimal high = guides.Max(guide => guide.Price);
        reasons.Add($"No matching sales. Priced from the {cheapest.Source} guide for {cheapest.Condition?.ToLowerInvariant() ?? "this condition"}.");
        return new Pricing(PriceBasis.Guide, cheapest.Price * 0.85m, cheapest.Price, high, 50, 60);
    }

    private static Pricing FromAsking(List<Comp> asking, List<string> reasons)
    {
        List<(decimal Value, double Weight)> points = asking
            .Select(listing => (listing.Price, listing.Verified ? 1.0 : UnverifiedWeight))
            .ToList();
        decimal list = asking.Count == 1 ? asking[0].Price * 0.9m : PriceMath.WeightedPercentile(points, 0.25);
        decimal high = asking.Count == 1 ? asking[0].Price : PriceMath.WeightedPercentile(points, 0.75);

        reasons.Add($"No matching sales. Priced under the cheaper of {Count(asking.Count, "current listing")}, because asking prices run above what items sell for.");
        int score = Math.Min(25, 6 * asking.Count) + SpreadPoints(asking, "Listings", 0.5, reasons);
        return new Pricing(PriceBasis.Asking, list * 0.8m, list, Math.Max(high, list), score, 45);
    }

    private static Pricing FromEstimate(ModelEstimate estimate, List<string> reasons)
    {
        decimal low = Math.Max(0, Math.Min(estimate.Low, estimate.High));
        decimal high = estimate.High;
        decimal list = estimate.IsWide ? low + ((high - low) * 0.6m) : (low + high) / 2;
        reasons.Add("No comps found. This is a model's estimate with no listings behind it.");
        int score = estimate.Confidence switch
        {
            "high" => 25,
            "medium" => 18,
            _ => 10,
        };
        return new Pricing(PriceBasis.Estimate, low > 0 ? low : list, list, high, score, 25);
    }

    private static Pricing NoPrice(List<string> reasons)
    {
        reasons.Add("No comps and no estimate. Check the comp sources and add what you find.");
        return new Pricing(PriceBasis.None, null, null, null, 0, 0);
    }

    private static AssessedComp Assess(Comp comp, MarketProfile item, IReadOnlyList<string> itemTokens)
    {
        if (comp.Price <= 0)
        {
            return new AssessedComp(comp, false, "no price");
        }

        MarketProfile profile = CompMatching.Profile(comp.Condition, comp.Title, comp.Url);
        IReadOnlyList<string> mismatches = CompMatching.Mismatches(item, profile);
        if (mismatches.Count > 0)
        {
            return new AssessedComp(comp, false, mismatches[0]);
        }

        string? condition = comp.Kind == CompKind.Guide
            ? GuideConditionMismatch(item.Condition, comp.Condition, profile.Condition)
            : CompMatching.ConditionMismatch(item.Condition, profile.Condition);
        if (condition is not null)
        {
            return new AssessedComp(comp, false, condition);
        }

        IReadOnlyList<string> compTokens = CompMatching.Tokens(comp.Title);
        if (compTokens.Count >= 2 && !CompMatching.NamesSameItem(itemTokens, compTokens))
        {
            return new AssessedComp(comp, false, "different item");
        }

        if (CompMatching.ModelConflict(itemTokens, compTokens))
        {
            return new AssessedComp(comp, false, "different model");
        }

        return new AssessedComp(comp, true, null);
    }

    // A guide prices every condition separately; an item of unknown condition is priced as used,
    // since that is what a reseller is almost always holding.
    private static string? GuideConditionMismatch(ConditionBand item, string? guideCondition, ConditionBand guide)
    {
        string label = guideCondition?.ToLowerInvariant() ?? string.Empty;
        if (label.Contains("box only", StringComparison.Ordinal) || label.Contains("manual only", StringComparison.Ordinal))
        {
            return "part of the item only";
        }

        ConditionBand wanted = item == ConditionBand.Unknown ? ConditionBand.Used : item;
        return CompMatching.ConditionMismatch(wanted, guide);
    }

    private static List<AssessedComp> TrimOutliers(List<AssessedComp> assessed, Func<Comp, bool> inPool)
    {
        List<int> pool = Enumerable.Range(0, assessed.Count)
            .Where(index => assessed[index].Included && inPool(assessed[index].Comp))
            .ToList();
        if (pool.Count < 3)
        {
            return assessed;
        }

        double[] logs = pool.Select(index => Math.Log((double)assessed[index].Comp.Price)).Order().ToArray();
        double median = Quantile(logs, 0.5);
        double lower = median - SmallPoolBand;
        double upper = median + SmallPoolBand;
        if (pool.Count >= 4)
        {
            double q1 = Quantile(logs, 0.25);
            double q3 = Quantile(logs, 0.75);
            double fence = 1.5 * (q3 - q1);
            lower = Math.Min(q1 - fence, median - OutlierFloorBand);
            upper = Math.Max(q3 + fence, median + OutlierFloorBand);
        }

        List<AssessedComp> trimmed = [.. assessed];
        foreach (int index in pool)
        {
            double value = Math.Log((double)assessed[index].Comp.Price);
            if (value < lower || value > upper)
            {
                trimmed[index] = assessed[index] with { Included = false, ExcludedBecause = "price outlier" };
            }
        }

        return trimmed;
    }

    private static int SpreadPoints(List<Comp> comps, string noun, double scale, List<string> reasons)
    {
        if (comps.Count < 2)
        {
            reasons.Add($"Only one {noun.ToLowerInvariant().TrimEnd('s')}, so nothing to check it against.");
            return 0;
        }

        List<decimal> prices = comps.Select(comp => comp.Price).Order().ToList();
        decimal median = PriceMath.Median(prices);
        decimal width = prices.Count >= 4
            ? PriceMath.PercentileNearestRank(prices, 0.75m) - PriceMath.PercentileNearestRank(prices, 0.25m)
            : prices[^1] - prices[0];
        decimal ratio = median > 0 ? width / median : 1;
        string range = $"({Money(prices[0])}–{Money(prices[^1])})";

        (int points, string phrase) = ratio switch
        {
            <= 0.20m => (20, "agree closely"),
            <= 0.45m => (12, "agree"),
            <= 0.80m => (5, "vary"),
            _ => (0, "vary widely"),
        };
        reasons.Add($"{noun} {phrase} {range}.");
        return (int)Math.Round(points * scale);
    }

    private static int RecencyPoints(List<Comp> sales, DateTimeOffset now, List<string> reasons)
    {
        List<double> ages = sales
            .Where(sale => sale.Date.HasValue)
            .Select(sale => Math.Max(0, (now - sale.Date!.Value).TotalDays))
            .Order()
            .ToList();
        if (ages.Count == 0)
        {
            reasons.Add("Sale dates are unknown.");
            return 0;
        }

        double typical = Quantile([.. ages], 0.5);
        (int points, string phrase) = typical switch
        {
            <= 45 => (15, "Sales are recent, mostly within six weeks."),
            <= 120 => (10, "Sales are from the last four months."),
            <= 365 => (4, "Sales are up to a year old."),
            _ => (0, "Sales are more than a year old."),
        };
        reasons.Add(phrase);
        return points;
    }

    private static int ConditionPoints(ConditionBand item, List<Comp> sales, List<string> reasons)
    {
        if (item == ConditionBand.Unknown)
        {
            reasons.Add("Condition not given, so sales in any condition count.");
            return 0;
        }

        return sales.Any(sale => CompMatching.BandOf(sale.Condition) == item) ? 5 : 0;
    }

    private static int IdentificationPenalty(IdentificationCertainty certainty, List<string> reasons)
    {
        switch (certainty)
        {
            case IdentificationCertainty.Medium:
                reasons.Add("Identification is likely but not certain. Confirm the model.");
                return 10;
            case IdentificationCertainty.Low:
                reasons.Add("Identification is a best guess. Confirm brand and model before relying on this price.");
                return 20;
            default:
                return 0;
        }
    }

    private static void AddExclusionSummary(List<AssessedComp> assessed, List<string> reasons)
    {
        List<IGrouping<string, AssessedComp>> groups = assessed
            .Where(comp => !comp.Included)
            .GroupBy(comp => comp.ExcludedBecause ?? "excluded")
            .OrderByDescending(group => group.Count())
            .ToList();
        if (groups.Count == 0)
        {
            return;
        }

        int total = groups.Sum(group => group.Count());
        string detail = string.Join(", ", groups.Select(group => $"{group.Count()} {group.Key}"));
        reasons.Add($"{Count(total, "comp")} left out: {detail}.");
    }

    private static double SaleWeight(Comp sale, DateTimeOffset now)
    {
        double recency = sale.Date is { } date
            ? Math.Max(MinimumRecencyWeight, Math.Pow(0.5, Math.Max(0, (now - date).TotalDays) / SaleHalfLifeDays))
            : UndatedWeight;
        double source = sale.Verified ? 1.0 : UnverifiedWeight;
        double kind = sale.Kind == CompKind.YourSale ? YourSaleWeight : 1.0;
        return recency * source * kind;
    }

    public static ConfidenceLevel LevelFor(int score, PriceBasis basis) => basis == PriceBasis.None
        ? ConfidenceLevel.None
        : score switch
        {
            >= 75 => ConfidenceLevel.Strong,
            >= 50 => ConfidenceLevel.Fair,
            >= 25 => ConfidenceLevel.Thin,
            _ => ConfidenceLevel.Guess,
        };

    private static IReadOnlyList<AssessedComp> Order(List<AssessedComp> assessed) => assessed
        .OrderByDescending(comp => comp.Included)
        .ThenBy(comp => comp.Comp.Kind)
        .ThenByDescending(comp => comp.Comp.Date ?? DateTimeOffset.MinValue)
        .ThenBy(comp => comp.Comp.Price)
        .ToArray();

    private static bool IsSale(Comp comp) => comp.Kind is CompKind.YourSale or CompKind.Sold;

    private static List<Comp> Included(List<AssessedComp> assessed, Func<Comp, bool> predicate) =>
        assessed.Where(comp => comp.Included && predicate(comp.Comp)).Select(comp => comp.Comp).ToList();

    private static double Quantile(double[] ordered, double q)
    {
        double position = (ordered.Length - 1) * q;
        int below = (int)Math.Floor(position);
        int above = (int)Math.Ceiling(position);
        return ordered[below] + ((ordered[above] - ordered[below]) * (position - below));
    }

    private static decimal? Round(decimal? value) => value is { } price ? PriceMath.ToMarketplaceEnding(price) : null;

    private static string Money(decimal value) => value.ToString("$#,0.##", CultureInfo.InvariantCulture);

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
