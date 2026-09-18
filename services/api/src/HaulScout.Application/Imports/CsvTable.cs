using System.Globalization;

namespace HaulScout.Application.Imports;

/// <summary>
/// A parsed CSV, addressed by HEADER NAME rather than column position. Marketplace reports reorder and add
/// columns between exports; positional parsing would silently shift money into the wrong field. Header lookup
/// makes a missing column a loud, named failure instead.
/// </summary>
public sealed class CsvTable
{
    private readonly Dictionary<string, int> _columns;
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
    public IReadOnlyList<string> Header { get; }

    private CsvTable(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        Header = header;
        Rows = rows;
        _columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            _columns.TryAdd(header[i].Trim(), i);
        }
    }

    public bool Has(string column) => _columns.ContainsKey(column);

    /// <summary>True when every one of these headers is present — the signature used to detect a report type.</summary>
    public bool HasAll(params string[] columns) => columns.All(Has);

    public string Text(IReadOnlyList<string> row, string column) =>
        _columns.TryGetValue(column, out int index) && index < row.Count ? row[index].Trim() : string.Empty;

    /// <summary>Parses marketplace money: "$1,234.56", "($12.34)" negative, "--", "" → 0.</summary>
    public decimal Money(IReadOnlyList<string> row, string column)
    {
        string text = Text(row, column);
        if (text.Length == 0) return 0;
        bool negative = text.StartsWith('(') && text.EndsWith(')');
        text = text.Replace("(", "").Replace(")", "").Replace("$", "").Replace(",", "").Replace("--", "").Trim();
        if (text.Length == 0 || text == "-") return 0;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)) return 0;
        return negative ? -value : value;
    }

    public string TextStartsWith(IReadOnlyList<string> row, string prefix)
    {
        for (int i = 0; i < Header.Count; i++)
        {
            if (Header[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return i < row.Count ? row[i].Trim() : string.Empty;
            }
        }
        return string.Empty;
    }

    public decimal MoneyStartsWith(IReadOnlyList<string> row, string prefix)
    {
        string text = TextStartsWith(row, prefix);
        if (text.Length == 0) return 0;
        bool negative = text.StartsWith('(') && text.EndsWith(')');
        text = text.Replace("(", "").Replace(")", "").Replace("$", "").Replace(",", "").Replace("--", "").Trim();
        if (text.Length == 0 || text == "-") return 0;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)) return 0;
        return negative ? -value : value;
    }

    /// <summary>
    /// Strict money: <c>true</c> with <c>null</c> for a cell that says "nothing" (empty, "N/A", "-",
    /// "--"), <c>true</c> with the amount for a readable figure, and <c>false</c> for anything else.
    ///
    /// <see cref="Money"/> turns an unreadable cell into 0 without a word, which is how a real amount
    /// can vanish from an import unnoticed. New parsers use this and report the row instead. Excel's
    /// text-literal form (<c>="$1.50"</c>, <c>="-"</c>), which some exports emit, is unwrapped first.
    /// </summary>
    public bool TryMoney(IReadOnlyList<string> row, string column, out decimal? value)
    {
        value = null;
        string text = Unwrap(Text(row, column));
        if (text.Length == 0 || text is "-" or "--" || text.Equals("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool negative = (text.StartsWith('(') && text.EndsWith(')')) || text.StartsWith('-');
        string digits = text.Trim('(', ')').TrimStart('-').Replace("$", "").Replace(",", "").Trim();
        if (!decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
        {
            return false;
        }

        value = negative ? -amount : amount;
        return true;
    }

    /// <summary>A cell that says "nothing" in any of the ways exports say it, as <c>null</c>.</summary>
    public string? Meaningful(IReadOnlyList<string> row, string column)
    {
        string text = Unwrap(Text(row, column));
        return text.Length == 0 || text is "-" or "--" || text.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            ? null
            : text;
    }

    /// <summary>Excel's text-literal form — <c>="value"</c> — reduced to its value.</summary>
    private static string Unwrap(string text) =>
        text.StartsWith("=\"", StringComparison.Ordinal) && text.EndsWith('"') && text.Length >= 3
            ? text[2..^1].Trim()
            : text;

    public int Int(IReadOnlyList<string> row, string column, int fallback = 0) =>
        int.TryParse(Text(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    public DateTimeOffset? Date(IReadOnlyList<string> row, string column)
    {
        string text = Text(row, column);
        if (text.Length == 0) return null;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    public string? TextOrNull(IReadOnlyList<string> row, string column)
    {
        string text = Text(row, column);
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Finds the header row by looking for one that contains every <paramref name="requiredHeaders"/>. Marketplace
    /// exports bury the header under preamble rows (eBay puts a comma line first; Poshmark uses six), so the header
    /// is located by content, never by a hardcoded line number.
    /// </summary>
    public static CsvTable? Load(string csv, params string[] requiredHeaders)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;

        var records = SplitRecords(csv).Select(ParseLine).ToList();
        for (int i = 0; i < records.Count && i < 40; i++)
        {
            var candidate = records[i];
            var lookup = candidate.Select(c => c.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (requiredHeaders.All(lookup.Contains))
            {
                var rows = records.Skip(i + 1)
                    .Where(row => row.Any(cell => cell.Trim().Length > 0))
                    .ToList();
                return new CsvTable(candidate, rows);
            }
        }
        return null;
    }

    // Records are newline separated, but a quoted field may itself contain newlines.
    private static IEnumerable<string> SplitRecords(string csv)
    {
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char c in csv.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (c == '"') { inQuotes = !inQuotes; current.Append(c); }
            else if (c == '\n' && !inQuotes) { yield return current.ToString(); current.Clear(); }
            else current.Append(c);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes) { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        // Strip the UTF-8 BOM some exports prepend to the first cell.
        if (fields.Count > 0) fields[0] = fields[0].TrimStart('﻿');
        return fields;
    }
}
