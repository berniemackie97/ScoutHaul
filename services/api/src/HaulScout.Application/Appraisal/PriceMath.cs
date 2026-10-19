namespace HaulScout.Application.Appraisal;

public static class PriceMath
{
    /// <summary>Nearest marketplace-style price ending in .99; under a dollar stays as it is.</summary>
    public static decimal ToMarketplaceEnding(decimal value)
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
            return up;
        }

        return Math.Abs(up - rounded) <= Math.Abs(rounded - down) ? up : down;
    }

    public static decimal Median(IReadOnlyList<decimal> ordered)
    {
        int middle = ordered.Count / 2;
        return ordered.Count % 2 == 0
            ? decimal.Round((ordered[middle - 1] + ordered[middle]) / 2, 2, MidpointRounding.AwayFromZero)
            : ordered[middle];
    }

    public static decimal PercentileNearestRank(IReadOnlyList<decimal> ordered, decimal percentile)
    {
        int rank = Math.Clamp((int)Math.Ceiling(percentile * ordered.Count), 1, ordered.Count);
        return ordered[rank - 1];
    }

    /// <summary>The value below which <paramref name="percentile"/> of the total weight lies.</summary>
    public static decimal WeightedPercentile(IReadOnlyList<(decimal Value, double Weight)> points, double percentile)
    {
        List<(decimal Value, double Weight)> ordered = points.Where(point => point.Weight > 0).OrderBy(point => point.Value).ToList();
        if (ordered.Count == 0)
        {
            throw new ArgumentException("At least one weighted point is required.", nameof(points));
        }

        double target = ordered.Sum(point => point.Weight) * percentile;
        double running = 0;
        foreach ((decimal value, double weight) in ordered)
        {
            running += weight;
            if (running >= target)
            {
                return value;
            }
        }

        return ordered[^1].Value;
    }
}
