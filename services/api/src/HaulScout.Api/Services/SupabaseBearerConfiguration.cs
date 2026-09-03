using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace HaulScout.Api.Services;

/// <summary>
/// How a Supabase access token is validated. Lives in its own type rather than inline in Program.cs
/// so the security-critical parts of it can be asserted in tests.
/// </summary>
public static class SupabaseBearerConfiguration
{
    /// <summary>
    /// The only signature algorithms a token may use — all asymmetric.
    ///
    /// A symmetric algorithm verifies with the same value used to sign, so accepting one means anyone
    /// who learns that value can mint a token for any user. Supabase signs with asymmetric keys, so
    /// nothing legitimate needs HS256 here and allowing it only opens the algorithm-confusion bypass.
    /// </summary>
    public static readonly string[] AllowedAlgorithms =
    [
        SecurityAlgorithms.EcdsaSha256,
        SecurityAlgorithms.EcdsaSha384,
        SecurityAlgorithms.EcdsaSha512,
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSha384,
        SecurityAlgorithms.RsaSha512,
    ];

    public static TokenValidationParameters BuildValidationParameters(
        SupabaseAuthOptions authOptions,
        SupabaseSigningKeyResolver signingKeyResolver) => new()
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = AllowedAlgorithms,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            IssuerSigningKeyResolver = signingKeyResolver.ResolveSigningKeys,
        };

    public static void Configure(
        JwtBearerOptions options,
        SupabaseAuthOptions authOptions,
        SupabaseSigningKeyResolver signingKeyResolver) =>
        options.TokenValidationParameters = BuildValidationParameters(authOptions, signingKeyResolver);
}
