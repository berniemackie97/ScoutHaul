namespace HaulScout.Api.Configuration;

/// <summary>
/// Loads local <c>.env</c> profile files into process environment variables for local development.
///
/// Why this exists: .NET does not read <c>.env</c> natively. Without this, configuration would
/// silently differ depending on how you launch the API (<c>dotnet run</c> vs docker-compose).
/// This makes local config deterministic and explicit — no hidden magic.
///
/// Production (Railway, Fly, etc.) injects real environment variables and ships no <c>.env</c>
/// file, so this is a no-op there. Existing process env vars are never overwritten, so platform
/// secrets always win over a stray local file.
/// </summary>
internal static class DotEnv
{
    public static void LoadForDevelopment(string contentRootPath)
    {
        IReadOnlyList<string> paths = FindEnvFiles(contentRootPath);
        if (paths.Count == 0) return;

        Dictionary<string, string> fileValues = new(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            LoadFile(path, fileValues);
        }

        foreach ((string key, string value) in fileValues)
        {
            // Never override a value already provided by the platform/shell.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static IReadOnlyList<string> FindEnvFiles(string startPath)
    {
        string? explicitFile = Environment.GetEnvironmentVariable("HAULSCOUT_ENV_FILE");
        if (!string.IsNullOrWhiteSpace(explicitFile))
        {
            string fullPath = Path.GetFullPath(explicitFile);
            return File.Exists(fullPath) ? [fullPath] : [];
        }

        DirectoryInfo? dir = new(startPath);
        for (int depth = 0; dir is not null && depth < 6; depth++)
        {
            string[] candidates = CandidateNames()
                .Select(name => Path.Combine(dir.FullName, name))
                .Where(File.Exists)
                .ToArray();
            if (candidates.Length > 0)
            {
                return candidates;
            }

            dir = dir.Parent;
        }

        return [];
    }

    private static IEnumerable<string> CandidateNames()
    {
        string? profile = Environment.GetEnvironmentVariable("HAULSCOUT_ENV");
        yield return ".env";
        yield return ".env.local";
        if (!string.IsNullOrWhiteSpace(profile))
        {
            string normalized = profile.Trim().ToLowerInvariant();
            yield return $".env.{normalized}";
            yield return $".env.{normalized}.local";
        }
    }

    private static void LoadFile(string path, Dictionary<string, string> values)
    {
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            // Strip matching surrounding quotes, if present.
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            // Later profile files override earlier base files. Shell/platform env still wins afterward.
            values[key] = value;
        }
    }
}
