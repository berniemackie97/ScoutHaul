namespace HaulScout.Application.Sync;

/// <summary>How sync runs describe themselves to the operator — one wording for files and scans alike.</summary>
public static class SyncText
{
    /// <summary>"3 new, 40 unchanged listings." — naming only what happened.</summary>
    public static string Counted(int created, int updated, int unchanged, string noun)
    {
        int total = created + updated + unchanged;
        if (total == 0)
        {
            return $"No {noun}s were imported.";
        }

        string[] parts = new[] { (created, "new"), (updated, "updated"), (unchanged, "unchanged") }
            .Where(part => part.Item1 > 0)
            .Select(part => $"{part.Item1} {part.Item2}")
            .ToArray();
        return $"{string.Join(", ", parts)} {noun}{Plural(total)}.";
    }

    public static string Plural(int count) => count == 1 ? string.Empty : "s";
}
