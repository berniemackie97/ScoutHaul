namespace HaulScout.Domain.Tax;

/// <summary>
/// County discretionary sales surtax, from DR-15DSS R. 11/25 (calendar year 2026). Each county's surtaxes are kept as
/// separate components with their own effective and expiration dates, so a rate on any date sums what was in force.
/// </summary>
public static class FloridaSurtax
{
    public const decimal StateRate = 0.06m;

    /// <summary>Surtax applies to the first $5,000 of a single item of tangible personal property (DR-15N, Line 15(a)).</summary>
    public const decimal ItemCap = 5000m;

    /// <summary>The table is DR-15DSS for this year. Dates outside it are computed with what is loaded and flagged.</summary>
    public const int TableYear = 2026;

    public const string TableSource = "DR-15DSS R. 11/25 (2026)";

    public sealed record Component(decimal Rate, DateOnly Effective, DateOnly? Expires);

    // Declared before anything that reads it: static initializers run in source order.
    private static readonly IReadOnlyDictionary<string, Component[]> Table = new Dictionary<string, Component[]>(StringComparer.Ordinal)
    {
        ["Alachua"] = [C(.005m, 2019, 1, 1, 2030), C(.01m, 2023, 1, 1, 2032)],
        ["Baker"] = [C(.01m, 1994, 1, 1)],
        ["Bay"] = [C(.005m, 2011, 1, 1, 2030), C(.005m, 2017, 1, 1, 2036)],
        ["Bradford"] = [C(.01m, 1993, 3, 1)],
        ["Brevard"] = [C(.005m, 2015, 1, 1, 2026), C(.005m, 2017, 1, 1, 2026)],
        ["Broward"] = [C(.01m, 2019, 1, 1, 2048)],
        ["Calhoun"] = [C(.01m, 1993, 1, 1), C(.005m, 2009, 1, 1, 2028)],
        ["Charlotte"] = [C(.01m, 1995, 4, 1, 2026)],
        ["Citrus"] = [],
        ["Clay"] = [C(.01m, 1990, 2, 1, 2039), C(.005m, 2021, 1, 1, 2050)],
        ["Collier"] = [],
        ["Columbia"] = [C(.01m, 1994, 8, 1), C(.005m, 2023, 1, 1, 2042)],
        ["DeSoto"] = [C(.01m, 1988, 1, 1), C(.005m, 2015, 1, 1, 2035)],
        ["Dixie"] = [C(.01m, 1990, 4, 1, 2029)],
        ["Duval"] = [C(.005m, 1989, 1, 1), C(.005m, 2001, 1, 1, 2030), C(.005m, 2021, 1, 1, 2035)],
        ["Escambia"] = [C(.01m, 1992, 6, 1, 2028), C(.005m, 1998, 1, 1, 2037)],
        ["Flagler"] = [C(.005m, 2003, 1, 1, 2032), C(.005m, 2013, 1, 1, 2032)],
        ["Franklin"] = [C(.01m, 2008, 1, 1), C(.005m, 2023, 1, 1, 2042)],
        ["Gadsden"] = [C(.01m, 1996, 1, 1), C(.005m, 2009, 1, 1, 2038)],
        ["Gilchrist"] = [C(.01m, 1992, 10, 1)],
        ["Glades"] = [C(.01m, 2022, 1, 1, 2031)],
        ["Gulf"] = [C(.01m, 2010, 1, 1)],
        ["Hamilton"] = [C(.01m, 1990, 7, 1, 2029), C(.01m, 2025, 1, 1, 2036)],
        ["Hardee"] = [C(.01m, 1998, 1, 1)],
        ["Hendry"] = [C(.01m, 1988, 1, 1), C(.005m, 2023, 1, 1, 2042)],
        ["Hernando"] = [C(.005m, 2016, 1, 1, 2035)],
        ["Highlands"] = [C(.01m, 1989, 11, 1, 2033), C(.005m, 2017, 1, 1, 2036)],
        ["Hillsborough"] = [C(.005m, 1996, 12, 1, 2041), C(.005m, 2001, 10, 1), C(.005m, 2019, 1, 1, 2028)],
        ["Holmes"] = [C(.01m, 1995, 10, 1, 2028), C(.005m, 2021, 1, 1, 2032)],
        ["Indian River"] = [C(.01m, 1989, 6, 1, 2034)],
        ["Jackson"] = [C(.01m, 1995, 6, 1), C(.005m, 1996, 7, 1, 2035)],
        ["Jefferson"] = [C(.01m, 1988, 6, 1)],
        ["Lafayette"] = [C(.01m, 1991, 9, 1)],
        ["Lake"] = [C(.01m, 1988, 1, 1, 2032)],
        ["Lee"] = [C(.005m, 2019, 1, 1, 2028)],
        ["Leon"] = [C(.01m, 1989, 12, 1, 2039), C(.005m, 2003, 1, 1, 2027)],
        ["Levy"] = [C(.01m, 1992, 10, 1)],
        ["Liberty"] = [C(.01m, 1992, 11, 1), C(.005m, 2017, 1, 1, 2026)],
        ["Madison"] = [C(.01m, 1989, 8, 1), C(.005m, 2007, 1, 1)],
        ["Manatee"] = [C(.005m, 2003, 1, 1, 2032), C(.005m, 2017, 1, 1, 2031)],
        ["Marion"] = [C(.01m, 2017, 1, 1, 2044), C(.005m, 2025, 1, 1, 2034)],
        ["Martin"] = [C(.005m, 2025, 1, 1, 2034)],
        ["Miami-Dade"] = [C(.005m, 1992, 1, 1), C(.005m, 2003, 1, 1)],
        ["Monroe"] = [C(.01m, 1989, 11, 1, 2048), C(.005m, 1996, 1, 1, 2035)],
        ["Nassau"] = [C(.01m, 1996, 3, 1)],
        ["Okaloosa"] = [C(.005m, 2019, 1, 1, 2028), C(.005m, 2021, 1, 1, 2030)],
        ["Okeechobee"] = [C(.01m, 1995, 10, 1)],
        ["Orange"] = [C(.005m, 2003, 1, 1, 2035)],
        ["Osceola"] = [C(.01m, 1990, 9, 1, 2045), C(.005m, 2017, 1, 1, 2036)],
        ["Palm Beach"] = [C(.005m, 2026, 1, 1, 2035)],
        ["Pasco"] = [C(.01m, 2005, 1, 1, 2039)],
        ["Pinellas"] = [C(.01m, 1990, 2, 1, 2029)],
        ["Polk"] = [C(.005m, 2004, 1, 1, 2033), C(.005m, 2005, 1, 1, 2044)],
        ["Putnam"] = [C(.01m, 2003, 1, 1, 2032)],
        ["St. Johns"] = [C(.005m, 2016, 1, 1, 2035)],
        ["St. Lucie"] = [C(.005m, 1996, 7, 1, 2036), C(.005m, 2019, 1, 1, 2028)],
        ["Santa Rosa"] = [C(.005m, 1998, 10, 1, 2028), C(.005m, 2017, 1, 1, 2026)],
        ["Sarasota"] = [C(.01m, 1989, 9, 1, 2039)],
        ["Seminole"] = [C(.01m, 2015, 1, 1, 2034)],
        ["Sumter"] = [C(.01m, 1993, 1, 1)],
        ["Suwannee"] = [C(.01m, 1988, 1, 1)],
        ["Taylor"] = [C(.01m, 1989, 8, 1, 2037)],
        ["Union"] = [C(.01m, 1993, 2, 1)],
        ["Volusia"] = [C(.005m, 2002, 1, 1, 2031)],
        ["Wakulla"] = [C(.01m, 1988, 1, 1, 2037), C(.005m, 2023, 1, 1, 2032)],
        ["Walton"] = [C(.01m, 1995, 2, 1)],
        ["Washington"] = [C(.01m, 1993, 11, 1), C(.005m, 2019, 1, 1, 2028)],
    };

    private static readonly IReadOnlyList<string> CountyNames = Table.Keys.Order(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<string> Counties => CountyNames;

    public static bool IsCounty(string? county) => county is not null && Table.ContainsKey(county);

    public static string? Canonical(string? county)
    {
        if (string.IsNullOrWhiteSpace(county)) return null;
        string trimmed = county.Trim();
        if (trimmed.EndsWith(" County", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^7].TrimEnd();
        string compact = Compact(trimmed);
        return Table.Keys.FirstOrDefault(key => Compact(key) == compact);
    }

    public static IReadOnlyList<Component> ComponentsOf(string county) =>
        Table.TryGetValue(county, out Component[]? components) ? components : throw new ArgumentException($"Unknown Florida county '{county}'.", nameof(county));

    public static decimal RateOn(string county, DateOnly day) =>
        ComponentsOf(county)
            .Where(component => component.Effective <= day && (component.Expires is null || day <= component.Expires))
            .Sum(component => component.Rate);

    public static bool Covers(DateOnly day) => day.Year == TableYear;

    private static string Compact(string value) =>
        new(value.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());

    private static Component C(decimal rate, int y, int m, int d, int? expiresYear = null) =>
        new(rate, new DateOnly(y, m, d), expiresYear is int year ? new DateOnly(year, 12, 31) : null);

}
