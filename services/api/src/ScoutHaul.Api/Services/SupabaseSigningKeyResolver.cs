using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace ScoutHaul.Api.Services;

public sealed class SupabaseSigningKeyResolver(SupabaseAuthOptions options)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private readonly HttpClient httpClient = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private DateTimeOffset expiresAt;
    private IReadOnlyCollection<SecurityKey> cachedKeys = [];

    public IEnumerable<SecurityKey> ResolveSigningKeys(string? token, SecurityToken? securityToken, string? kid, TokenValidationParameters validationParameters)
    {
        IReadOnlyCollection<SecurityKey> keys = GetKeys();
        if (string.IsNullOrWhiteSpace(kid))
        {
            return keys;
        }

        SecurityKey[] matches = keys.Where(key => string.Equals(key.KeyId, kid, StringComparison.Ordinal)).ToArray();
        return matches.Length > 0 ? matches : keys;
    }

    private IReadOnlyCollection<SecurityKey> GetKeys()
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
            using var document = JsonDocument.Parse(stream);
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
