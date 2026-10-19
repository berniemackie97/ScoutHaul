using System.Text.RegularExpressions;

namespace HaulScout.Application.Services;

public static class ResaleSearchHintBuilder
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static ResaleSearchHints FromQuery(string query)
    {
        string normalized = NormalizeQuery(query);
        string encoded = Uri.EscapeDataString(normalized);
        string url = $"https://www.ebay.com/sch/i.html?_nkw={encoded}&LH_Sold=1&LH_Complete=1&_sop=13";
        return new ResaleSearchHints(normalized, url);
    }

    private static string NormalizeQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = WhitespaceRegex.Replace(value, " ").Trim();
        const int maxLength = 180;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].Trim();
    }
}
