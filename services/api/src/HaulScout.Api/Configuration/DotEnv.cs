namespace HaulScout.Api.Configuration;

/// <summary>
/// Loads a local <c>.env</c> file into process environment variables for local development.
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
        // Walk up from the content root to find a .env (repo layouts vary: src/Api vs services/api).
        string? path = FindEnvFile(contentRootPath);
        if (path is null) return;

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

            // Never override a value already provided by the platform/shell.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? FindEnvFile(string startPath)
    {
        DirectoryInfo? dir = new(startPath);
        for (int depth = 0; dir is not null && depth < 6; depth++)
        {
            string candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
