using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace HaulScout.Api.Services;

/// <summary>
/// Resolves the keys a Supabase token may be signed with, from the project's published JWKS.
///
/// This used to also trust a shared secret from <c>Authentication:Supabase:JwtSecret</c>, turned into
/// an HS256 symmetric key. That was a signing bypass waiting to happen: a symmetric key verifies
/// tokens with the same value used to mint them, so anyone who learned that value could forge a token
/// for any user. The value the development config carried was in fact the project's public key id —
/// published in the JWKS, readable by anyone. Supabase signs with asymmetric keys (this project uses
/// ES256), so the legacy path bought nothing and is gone. <c>ConfigureSupabaseBearer</c> pins the
/// accepted algorithms to the asymmetric set so a symmetric token cannot be introduced again.
/// </summary>
public sealed class SupabaseSigningKeyResolver(SupabaseAuthOptions options)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private readonly HttpClient httpClient = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private DateTimeOffset expiresAt;
    private IReadOnlyCollection<SecurityKey> cachedKeys = [];

    public IEnumerable<SecurityKey> ResolveSigningKeys(string? token, SecurityToken? securityToken, string? kid, TokenValidationParameters validationParameters)
    {
        IReadOnlyCollection<SecurityKey> keys = GetJwksKeys();
        if (string.IsNullOrWhiteSpace(kid))
        {
            return keys;
        }

        SecurityKey[] matches = keys.Where(key => string.Equals(key.KeyId, kid, StringComparison.Ordinal)).ToArray();
        return matches.Length > 0 ? matches : keys;
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
