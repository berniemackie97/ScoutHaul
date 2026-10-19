namespace HaulScout.Domain.Tax;

/// <summary>
/// Florida's floating interest on late payments and deficiencies (s. 213.235, F.S.), daily factors from TIP 26ADM-02.
/// Days run from the day after the late-after date through the payment date inclusive; each rate interval is its own
/// rounded segment, exactly as the TIP's examples compute.
/// </summary>
public static class FloridaInterest
{
    public sealed record RateInterval(DateOnly From, DateOnly To, decimal AnnualRate, decimal DailyFactor);

    public sealed record Segment(DateOnly From, DateOnly To, int Days, decimal DailyFactor, decimal Amount, bool Estimated);

    public sealed record Result(decimal Amount, int Days, IReadOnlyList<Segment> Segments)
    {
        public bool Estimated => Segments.Any(segment => segment.Estimated);
    }

    public const string TableSource = "TIP 26ADM-02";

    public static IReadOnlyList<RateInterval> Intervals { get; } =
    [
        new(new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31), .09m, 0.000245902m),
        new(new DateOnly(2021, 1, 1), new DateOnly(2022, 12, 31), .07m, 0.000191781m),
        new(new DateOnly(2023, 1, 1), new DateOnly(2023, 6, 30), .09m, 0.000246575m),
        new(new DateOnly(2023, 7, 1), new DateOnly(2023, 12, 31), .11m, 0.000301370m),
        new(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), .12m, 0.000327869m),
        new(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), .12m, 0.000328767m),
        new(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), .11m, 0.000301370m),
    ];

    public static Result Compute(decimal taxDue, DateOnly lateAfter, DateOnly paidOn)
    {
        if (taxDue <= 0 || paidOn <= lateAfter) return new Result(0m, 0, []);

        var segments = new List<Segment>();
        DateOnly cursor = lateAfter.AddDays(1);
        while (cursor <= paidOn)
        {
            (RateInterval interval, bool estimated) = IntervalFor(cursor);
            DateOnly to = interval.To < paidOn ? interval.To : paidOn;
            int days = to.DayNumber - cursor.DayNumber + 1;
            decimal amount = FloridaTaxMath.Cents(taxDue * days * interval.DailyFactor);
            segments.Add(new Segment(cursor, to, days, interval.DailyFactor, amount, estimated));
            cursor = to.AddDays(1);
        }

        return new Result(segments.Sum(segment => segment.Amount), segments.Sum(segment => segment.Days), segments);
    }

    private static (RateInterval Interval, bool Estimated) IntervalFor(DateOnly day)
    {
        RateInterval? known = Intervals.FirstOrDefault(interval => interval.From <= day && day <= interval.To);
        if (known is not null) return (known, false);

        // Published each January and July; past the table, the latest known rate stands in and the result says so.
        return day > Intervals[^1].To
            ? (Intervals[^1] with { From = day, To = DateOnly.MaxValue }, true)
            : (Intervals[0] with { From = day, To = Intervals[0].From.AddDays(-1) }, true);
    }
}
