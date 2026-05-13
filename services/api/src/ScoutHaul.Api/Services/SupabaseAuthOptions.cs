namespace ScoutHaul.Api.Services;

public sealed record SupabaseAuthOptions(string ProjectUrl, string Issuer, string Audience, string? JwtSecret, string JwksUrl)
{
    public static SupabaseAuthOptions? FromConfiguration(IConfiguration configuration)
    {
        string? projectUrl = configuration["Authentication:Supabase:Url"];
        if (string.IsNullOrWhiteSpace(projectUrl))
        {
            return null;
        }

        projectUrl = projectUrl.Trim().TrimEnd('/');
        string issuer = configuration["Authentication:Supabase:Issuer"] ?? $"{projectUrl}/auth/v1";
        string audience = configuration["Authentication:Supabase:Audience"] ?? "authenticated";
        string? jwtSecret = configuration["Authentication:Supabase:JwtSecret"];
        string jwksUrl = configuration["Authentication:Supabase:JwksUrl"] ?? $"{projectUrl}/auth/v1/.well-known/jwks.json";

        return new SupabaseAuthOptions(projectUrl, issuer.TrimEnd('/'), audience, jwtSecret, jwksUrl);
    }
}
