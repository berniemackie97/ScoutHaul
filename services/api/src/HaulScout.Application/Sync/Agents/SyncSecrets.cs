using System.Security.Cryptography;
using System.Text;

namespace HaulScout.Application.Sync.Agents;

/// <summary>
/// Device tokens and pairing codes: how they are made, recognised and stored.
///
/// Only hashes are stored. A token is 256 random bits, so a plain SHA-256 is the right hash — there
/// is nothing to brute-force. A pairing code is short enough to type, which is why it lives minutes,
/// works once, and is exchanged through a rate-limited endpoint.
/// </summary>
public static class SyncSecrets
{
    /// <summary>Marks a device token, so one pasted in the wrong place is recognisable.</summary>
    public const string TokenPrefix = "hsa_";

    // Crockford's base32: no I, L, O or U, so a code read aloud or copied by hand survives.
    private const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int CodeLength = 8;

    public static string NewToken() => TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    public static bool LooksLikeToken(string? value) =>
        value is not null && value.StartsWith(TokenPrefix, StringComparison.Ordinal) && value.Length >= TokenPrefix.Length + 40;

    public static string HashToken(string token) => Sha256Hex(token);

    /// <summary>A new code, grouped for reading: <c>7KQ2-MX9D</c>.</summary>
    public static string NewPairingCode()
    {
        char[] code = new char[CodeLength];
        for (int index = 0; index < CodeLength; index++)
        {
            code[index] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return $"{new string(code, 0, 4)}-{new string(code, 4, 4)}";
    }

    /// <summary>
    /// The code as typed, made canonical — case, spaces and dashes ignored, and the letters people
    /// mistake for digits read as the digits. Null when it cannot be a code at all.
    /// </summary>
    public static string? NormalizePairingCode(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        StringBuilder code = new(CodeLength);
        foreach (char raw in typed.ToUpperInvariant())
        {
            if (char.IsWhiteSpace(raw) || raw == '-')
            {
                continue;
            }

            char normalized = raw switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => raw,
            };
            if (!CodeAlphabet.Contains(normalized, StringComparison.Ordinal))
            {
                return null;
            }

            code.Append(normalized);
        }

        return code.Length == CodeLength ? code.ToString() : null;
    }

    /// <summary>Hash of a normalized code. Namespaced so a code can never collide with a token hash.</summary>
    public static string HashPairingCode(string normalizedCode) => Sha256Hex("pairing:" + normalizedCode);

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
