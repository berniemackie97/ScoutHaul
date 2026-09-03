using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using HaulScout.Api.Services;

namespace HaulScout.Tests;

/// <summary>
/// Guards the token-validation rules. These are the settings that decide whether a forged token is
/// accepted, so each one is pinned here rather than left to survive on review attention alone.
/// </summary>
public sealed class SupabaseAuthConfigurationTests
{
    private static SupabaseAuthOptions Options() =>
        SupabaseAuthOptions.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Supabase:Url"] = "https://project.supabase.co",
                })
                .Build())!;

    [Fact]
    public void AcceptsNoSymmetricAlgorithm()
    {
        // A symmetric key verifies with the same value that signs, so accepting one means anyone who
        // learns that value can mint a token for any user. Supabase signs asymmetrically.
        Assert.DoesNotContain(
            SupabaseBearerConfiguration.AllowedAlgorithms,
            algorithm => algorithm.StartsWith("HS", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsTheAlgorithmsSupabaseActuallySigns()
    {
        Assert.Contains(SecurityAlgorithms.EcdsaSha256, SupabaseBearerConfiguration.AllowedAlgorithms);
        Assert.Contains(SecurityAlgorithms.RsaSha256, SupabaseBearerConfiguration.AllowedAlgorithms);
    }

    [Fact]
    public void PinsTheAlgorithmListOntoTheValidationParameters()
    {
        SupabaseAuthOptions options = Options();
        TokenValidationParameters parameters =
            SupabaseBearerConfiguration.BuildValidationParameters(options, new SupabaseSigningKeyResolver(options));

        // A null list means "anything the key supports", which is how the bypass gets back in.
        Assert.NotNull(parameters.ValidAlgorithms);
        Assert.Equal(SupabaseBearerConfiguration.AllowedAlgorithms, parameters.ValidAlgorithms);
    }

    [Fact]
    public void ValidatesIssuerAudienceLifetimeAndSigningKey()
    {
        SupabaseAuthOptions options = Options();
        TokenValidationParameters parameters =
            SupabaseBearerConfiguration.BuildValidationParameters(options, new SupabaseSigningKeyResolver(options));

        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.Equal("https://project.supabase.co/auth/v1", parameters.ValidIssuer);
        Assert.Equal("authenticated", parameters.ValidAudience);
    }

    [Fact]
    public void DerivesTheJwksUrlFromTheProject()
    {
        Assert.Equal(
            "https://project.supabase.co/auth/v1/.well-known/jwks.json",
            Options().JwksUrl);
    }

    [Fact]
    public void HasNoSharedSecretSettingToMisconfigure()
    {
        // The old Authentication:Supabase:JwtSecret became a trusted HS256 key whenever it was set,
        // and the value the development config carried was in fact the project's public key id. The
        // setting is gone; this fails if anyone reintroduces it.
        Assert.DoesNotContain(
            typeof(SupabaseAuthOptions).GetProperties(),
            property => property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }
}
