namespace HaulScout.Domain;

/// <summary>
/// Deterministic (no AI) title similarity used to SUGGEST which inventory item a scraped marketplace order line
/// refers to when there is no platform listing id to key on. Marketplace titles are long and keyword-stuffed
/// ("Vintage Levi's 501 Denim Jacket Men's M Distressed USA") while inventory titles are short ("Levi's 501
/// Jacket"), so the score is token containment against the shorter title — never a wrong-confident match, always
/// operator-confirmed.
/// </summary>
public static class TitleMatch
{
    // Filler that carries no identity; dropped before scoring so it can't inflate overlap.
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "size", "new", "nwt", "nwot", "vtg", "vintage", "mens", "womens",
        "men", "women", "unisex", "lot", "of", "in", "us", "usa", "free", "shipping", "rare", "authentic",
    };

    public static IReadOnlyList<string> Tokenize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        List<string> tokens = [];
        foreach (string raw in title.ToLowerInvariant().Split(
            [' ', '\t', '\n', '\r', '-', '_', '/', '\\', ',', '.', '(', ')', '[', ']', '{', '}', ':', ';', '\'', '"', '!', '?', '&', '+', '#', '*', '|', '@'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Length >= 2 && !Stopwords.Contains(raw))
            {
                tokens.Add(raw);
            }
        }
        return tokens;
    }

    /// <summary>0..1 similarity: how much of the shorter title's identity tokens appear in the other.</summary>
    public static double Score(string? a, string? b)
    {
        HashSet<string> tokensA = Tokenize(a).ToHashSet(StringComparer.Ordinal);
        HashSet<string> tokensB = Tokenize(b).ToHashSet(StringComparer.Ordinal);
        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0;
        }

        int overlap = tokensA.Count(tokensB.Contains);
        int shorter = Math.Min(tokensA.Count, tokensB.Count);
        return (double)overlap / shorter;
    }

    /// <summary>Best candidate id for <paramref name="query"/>, or null if none clears the bar. Requires both a
    /// strong containment score and at least two shared identity tokens so a single common word can't match.</summary>
    public static Guid? Best(string? query, IEnumerable<(Guid Id, string Title)> candidates, double threshold = 0.6)
    {
        HashSet<string> queryTokens = Tokenize(query).ToHashSet(StringComparer.Ordinal);
        if (queryTokens.Count == 0)
        {
            return null;
        }

        Guid? best = null;
        double bestScore = 0;
        foreach ((Guid Id, string Title) candidate in candidates)
        {
            HashSet<string> candidateTokens = Tokenize(candidate.Title).ToHashSet(StringComparer.Ordinal);
            int overlap = candidateTokens.Count(queryTokens.Contains);
            if (overlap < 2)
            {
                continue;
            }
            double score = (double)overlap / Math.Min(queryTokens.Count, candidateTokens.Count);
            if (score >= threshold && score > bestScore)
            {
                bestScore = score;
                best = candidate.Id;
            }
        }
        return best;
    }
}
