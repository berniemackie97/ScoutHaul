using System.Text;
using System.Text.RegularExpressions;

namespace HaulScout.Domain;

/// <summary>
/// A closed list of terms and the other ways people and marketplaces write them. Text from a form, a sync or an import
/// becomes a listed term or nothing, so filters, reports and listings all see one spelling.
/// </summary>
public sealed partial class Vocabulary
{
    private readonly Dictionary<string, string> _spellings = new(StringComparer.Ordinal);

    public Vocabulary(IEnumerable<string> terms, IReadOnlyDictionary<string, string>? otherSpellings = null)
    {
        Terms = terms.Where(term => !string.IsNullOrWhiteSpace(term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string term in Terms)
        {
            _spellings.TryAdd(Words(term), term);
        }

        foreach ((string spelling, string meant) in otherSpellings ?? new Dictionary<string, string>())
        {
            if (Terms.FirstOrDefault(term => string.Equals(term, meant, StringComparison.OrdinalIgnoreCase)) is string term)
            {
                _spellings.TryAdd(Words(spelling), term);
            }
        }
    }

    public IReadOnlyList<string> Terms { get; }

    /// <summary>
    /// The term one value names: the term itself in any case or punctuation, a known other spelling, or else the
    /// longest term or spelling it contains as whole words, the earliest written on a tie — "100% cotton" is Cotton,
    /// "navy blue" is Navy.
    /// </summary>
    public string? Canonical(string? value)
    {
        string words = Words(value);
        if (words.Length == 0)
        {
            return null;
        }

        if (_spellings.TryGetValue(words, out string? exact))
        {
            return exact;
        }

        string padded = $" {words} ";
        return _spellings
            .Select(entry => (entry.Value, Length: entry.Key.Length, At: padded.IndexOf($" {entry.Key} ", StringComparison.Ordinal)))
            .Where(match => match.At >= 0)
            .OrderByDescending(match => match.Length)
            .ThenBy(match => match.At)
            .Select(match => match.Value)
            .FirstOrDefault();
    }

    /// <summary>
    /// The terms values name, where one value may list several ("Silk/polyester", "Blue, White", "black and gold"):
    /// in the order written, each once, at most <paramref name="max"/>.
    /// </summary>
    public IReadOnlyList<string> CanonicalList(IEnumerable<string?> values, int max) =>
        values
            .SelectMany(value => ListSeparator().Split(value ?? string.Empty))
            .Select(Canonical)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(max)
            .ToArray();

    /// <summary>Lower case, letters and digits only, one space between words: the form two spellings are compared in.</summary>
    private static string Words(string? value)
    {
        StringBuilder words = new();
        foreach (char character in value ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
            {
                words.Append(char.ToLowerInvariant(character));
            }
            else if (words.Length > 0 && words[^1] != ' ')
            {
                words.Append(' ');
            }
        }

        return words.ToString().TrimEnd();
    }

    [GeneratedRegex(@"\s*(?:[,/&+;|]|\band\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ListSeparator();
}
