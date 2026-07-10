using System.Text.Json;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HaulScout.Api.Services;

public sealed class SupabaseSigningKeyResolver(SupabaseAuthOptions options)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private readonly HttpClient httpClient = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly SecurityKey? legacyJwtSecretKey = string.IsNullOrWhiteSpace(options.JwtSecret)
        ? null
        : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtSecret))
        {
            KeyId = "supabase-legacy-jwt-secret"
        };
    private DateTimeOffset expiresAt;
    private IReadOnlyCollection<SecurityKey> cachedKeys = [];

    public IEnumerable<SecurityKey> ResolveSigningKeys(string? token, SecurityToken? securityToken, string? kid, TokenValidationParameters validationParameters)
    {
        IReadOnlyCollection<SecurityKey> keys = GetKeysIncludingLegacySecret();
        if (string.IsNullOrWhiteSpace(kid))
        {
            return keys;
        }

        SecurityKey[] matches = keys.Where(key => string.Equals(key.KeyId, kid, StringComparison.Ordinal)).ToArray();
        return matches.Length > 0 ? matches : keys;
    }

    private IReadOnlyCollection<SecurityKey> GetKeysIncludingLegacySecret()
    {
        IReadOnlyCollection<SecurityKey> jwksKeys = GetJwksKeys();
        return legacyJwtSecretKey is null
            ? jwksKeys
            : [.. jwksKeys, legacyJwtSecretKey];
    }

    private IReadOnlyCollection<SecurityKey> GetJwksKeys()
    {
        if (cachedKeys.Count > 0 && expiresAt > DateTimeOffset.UtcNow)
        {
            return cachedKeys;
        }

        refreshLock.Wait();
        try
        {
            if (cachedKeys.Count > 0 && expiresAt > DateTimeOffset.UtcNow)
            {
                return cachedKeys;
            }

            using Stream stream = httpClient.GetStreamAsync(options.JwksUrl).GetAwaiter().GetResult();
            using JsonDocument document = JsonDocument.Parse(stream);
            string jwks = document.RootElement.GetRawText();
            cachedKeys = new JsonWebKeySet(jwks).GetSigningKeys().ToArray();
            expiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
            return cachedKeys;
        }
        finally
        {
            refreshLock.Release();
        }
    }
}
