namespace HaulScout.Domain.Tax;

public enum SalesTaxFrequency
{
    Monthly = 1,
    Quarterly = 3,
    Semiannual = 6,
    Annual = 12,
}

/// <summary>Alternate = quarters ending Feb/May/Aug/Nov, halves ending May/Nov (s. 212.11(1)(d), F.S.).</summary>
public enum SalesTaxPeriodAlignment
{
    Calendar,
    Alternate,
}

/// <summary>A filing frequency DOR assigned, from the first day of a period onward.</summary>
public sealed record SalesTaxFrequencySpan(DateOnly EffectiveFrom, SalesTaxFrequency Frequency, SalesTaxPeriodAlignment Alignment);

public sealed record SalesTaxPeriod(
    DateOnly Start,
    DateOnly End,
    SalesTaxFrequency Frequency,
    SalesTaxPeriodAlignment Alignment,
    bool IsFinal,
    DateOnly DueDate,
    DateOnly LateAfter,
    DateOnly ElectronicPaymentDeadline)
{
    public string Key => $"{Start:yyyyMMdd}-{End:yyyyMMdd}";

    public bool Contains(DateOnly day) => day >= Start && day <= End;
}

/// <summary>
/// Florida DR-15 reporting periods and deadlines. Returns are due the 1st of the month after the period and late
/// after the 20th, moved forward past weekends and holidays; electronic payment must be initiated by 5 p.m. ET on
/// the business day before the late-after date (DR-15N, DR-659). A final return is late 15 days after closing.
/// </summary>
public static class FloridaFilingCalendar
{
    public const int LateAfterDayOfMonth = 20;
    public const int FinalReturnDays = 15;

    /// <summary>
    /// Every period from the one containing <paramref name="registeredOn"/> through the one containing
    /// <paramref name="through"/>, or through <paramref name="closedOn"/> when the business closed.
    /// </summary>
    public static IReadOnlyList<SalesTaxPeriod> Periods(
        DateOnly registeredOn,
        IReadOnlyList<SalesTaxFrequencySpan> schedule,
        DateOnly through,
        DateOnly? closedOn = null)
    {
        if (schedule.Count == 0) throw new ArgumentException("A filing frequency is required.", nameof(schedule));

        List<SalesTaxFrequencySpan> spans = schedule.OrderBy(span => span.EffectiveFrom).ToList();
        DateOnly last = closedOn is DateOnly closed && closed < through ? closed : through;
        var periods = new List<SalesTaxPeriod>();
        DateOnly cursor = registeredOn;

        while (cursor <= last)
        {
            SalesTaxFrequencySpan span = SpanAt(spans, cursor);
            (DateOnly start, DateOnly end) = Containing(cursor, span.Frequency, span.Alignment);

            if (spans.FirstOrDefault(next => next.EffectiveFrom > cursor && next.EffectiveFrom <= end) is { } change)
            {
                end = change.EffectiveFrom.AddDays(-1);
            }

            bool isFinal = closedOn is DateOnly closing && closing <= end;
            if (isFinal) end = closedOn!.Value;

            periods.Add(Build(start, end, span.Frequency, span.Alignment, isFinal));
            if (isFinal) break;
            cursor = end.AddDays(1);
        }

        return periods;
    }

    public static SalesTaxPeriod Build(DateOnly start, DateOnly end, SalesTaxFrequency frequency, SalesTaxPeriodAlignment alignment, bool isFinal)
    {
        DateOnly due;
        DateOnly lateAfter;
        if (isFinal)
        {
            due = end.AddDays(1);
            lateAfter = FloridaHolidays.OnOrNextBusinessDay(end.AddDays(FinalReturnDays));
        }
        else
        {
            DateOnly following = new DateOnly(end.Year, end.Month, 1).AddMonths(1);
            due = following;
            lateAfter = FloridaHolidays.OnOrNextBusinessDay(following.AddDays(LateAfterDayOfMonth - 1));
        }

        return new SalesTaxPeriod(start, end, frequency, alignment, isFinal, due, lateAfter, FloridaHolidays.PreviousBusinessDay(lateAfter));
    }

    /// <summary>The canonical period of this frequency that contains <paramref name="day"/>.</summary>
    public static (DateOnly Start, DateOnly End) Containing(DateOnly day, SalesTaxFrequency frequency, SalesTaxPeriodAlignment alignment)
    {
        int months = (int)frequency;
        int offset = alignment == SalesTaxPeriodAlignment.Alternate ? -1 : 0;
        int index = day.Year * 12 + day.Month - 1;
        int startIndex = index - Mod(index - offset, months);
        var start = new DateOnly(startIndex / 12, startIndex % 12 + 1, 1);
        return (start, start.AddMonths(months).AddDays(-1));
    }

    public static bool SupportsAlternate(SalesTaxFrequency frequency) =>
        frequency is SalesTaxFrequency.Quarterly or SalesTaxFrequency.Semiannual;

    /// <summary>A frequency can only start on the first day of one of its own periods.</summary>
    public static bool IsPeriodStart(DateOnly day, SalesTaxFrequency frequency, SalesTaxPeriodAlignment alignment) =>
        Containing(day, frequency, alignment).Start == day;

    /// <summary>
    /// The least frequent schedule s. 212.11(1)(c) allows for the tax remitted over the prior four quarters. Only DOR
    /// changes a dealer's frequency; this is advice for the operator.
    /// </summary>
    public static SalesTaxFrequency EligibleFrequency(decimal taxRemittedPriorFourQuarters) => taxRemittedPriorFourQuarters switch
    {
        <= 100m => SalesTaxFrequency.Annual,
        <= 500m => SalesTaxFrequency.Semiannual,
        <= 1000m => SalesTaxFrequency.Quarterly,
        _ => SalesTaxFrequency.Monthly,
    };

    private static SalesTaxFrequencySpan SpanAt(List<SalesTaxFrequencySpan> spans, DateOnly day) =>
        spans.LastOrDefault(span => span.EffectiveFrom <= day) ?? spans[0];

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
