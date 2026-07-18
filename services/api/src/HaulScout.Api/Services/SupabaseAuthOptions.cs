namespace HaulScout.Api.Services;

public sealed record SupabaseAuthOptions(string ProjectUrl, string Issuer, string Audience, string? JwtSecret, string JwksUrl)
{
    public static SupabaseAuthOptions? FromConfiguration(
        IConfiguration configuration,
        string sectionPath = "Authentication:Supabase")
    {
        IConfiguration section = configuration.GetSection(sectionPath);
        string? projectUrl = section["Url"];
        if (string.IsNullOrWhiteSpace(projectUrl))
        {
            return null;
        }

        projectUrl = projectUrl.Trim().TrimEnd('/');
        string issuer = section["Issuer"] ?? $"{projectUrl}/auth/v1";
        string audience = section["Audience"] ?? "authenticated";
        string? jwtSecret = section["JwtSecret"];
        string jwksUrl = section["JwksUrl"] ?? $"{projectUrl}/auth/v1/.well-known/jwks.json";

        return new SupabaseAuthOptions(projectUrl, issuer.TrimEnd('/'), audience, jwtSecret, jwksUrl);
    }
}
