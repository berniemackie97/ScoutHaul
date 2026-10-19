using System.Collections.Concurrent;

namespace HaulScout.Domain.Tax;

/// <summary>
/// State (s. 110.117, F.S.) and federal holidays as Florida DOR observes them for due dates (DR-659):
/// Saturday is observed Friday, Sunday the following Monday.
/// </summary>
public static class FloridaHolidays
{
    private static readonly ConcurrentDictionary<int, IReadOnlyDictionary<DateOnly, string>> Cache = new();

    public static bool IsHoliday(DateOnly day) => NameOf(day) is not null;

    public static string? NameOf(DateOnly day) =>
        ObservedIn(day.Year).TryGetValue(day, out string? name) || ObservedIn(day.Year + 1).TryGetValue(day, out name)
            ? name
            : null;

    public static bool IsBusinessDay(DateOnly day) =>
        day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(day);

    public static DateOnly OnOrNextBusinessDay(DateOnly day)
    {
        while (!IsBusinessDay(day)) day = day.AddDays(1);
        return day;
    }

    public static DateOnly PreviousBusinessDay(DateOnly day)
    {
        do day = day.AddDays(-1);
        while (!IsBusinessDay(day));
        return day;
    }

    /// <summary>Holidays observed for the given holiday year; New Year's Day on a Saturday lands on Dec 31 of the year before.</summary>
    public static IReadOnlyDictionary<DateOnly, string> ObservedIn(int year) => Cache.GetOrAdd(year, Build);

    private static IReadOnlyDictionary<DateOnly, string> Build(int year)
    {
        var days = new Dictionary<DateOnly, string>
        {
            [Observed(new DateOnly(year, 1, 1))] = "New Year's Day",
            [NthWeekday(year, 1, DayOfWeek.Monday, 3)] = "Martin Luther King Jr. Day",
            [NthWeekday(year, 2, DayOfWeek.Monday, 3)] = "Presidents' Day",
            [LastWeekday(year, 5, DayOfWeek.Monday)] = "Memorial Day",
            [Observed(new DateOnly(year, 7, 4))] = "Independence Day",
            [NthWeekday(year, 9, DayOfWeek.Monday, 1)] = "Labor Day",
            [NthWeekday(year, 10, DayOfWeek.Monday, 2)] = "Columbus Day",
            [Observed(new DateOnly(year, 11, 11))] = "Veterans' Day",
            [Observed(new DateOnly(year, 12, 25))] = "Christmas Day",
        };

        if (year >= 2021)
        {
            days[Observed(new DateOnly(year, 6, 19))] = "Juneteenth";
        }

        DateOnly thanksgiving = NthWeekday(year, 11, DayOfWeek.Thursday, 4);
        days[thanksgiving] = "Thanksgiving Day";
        days[thanksgiving.AddDays(1)] = "Friday after Thanksgiving";
        return days;
    }

    private static DateOnly Observed(DateOnly day) => day.DayOfWeek switch
    {
        DayOfWeek.Saturday => day.AddDays(-1),
        DayOfWeek.Sunday => day.AddDays(1),
        _ => day,
    };

    private static DateOnly NthWeekday(int year, int month, DayOfWeek weekday, int nth)
    {
        var first = new DateOnly(year, month, 1);
        int offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + (nth - 1) * 7);
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek weekday)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        int back = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
        return last.AddDays(-back);
    }
}
