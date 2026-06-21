using System.Security.Cryptography;
using System.Text;

namespace LupiraLocationApi.Domain;

/// <summary>Pure helpers for the per-device ingest credential <c>{keyId:N}.{secret}</c>. The secret is 32 bytes of
/// CSPRNG entropy (hex), shown once at registration; only its SHA-256 hash is stored. A 256-bit random secret makes an
/// unsalted hash safe (rainbow tables are infeasible). Comparison is constant-time.</summary>
public static class DeviceKeyHashing
{
    /// <summary>Mints a fresh credential. Returns the public key id, the one-time plaintext secret, and the stored hash.</summary>
    public static (Guid KeyId, string Secret, string Hash) Mint()
    {
        var keyId = Guid.NewGuid();
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return (keyId, secret, Hash(secret));
    }

    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public static bool Verify(string secret, string expectedHash)
    {
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        byte[] expected;
        try { expected = Convert.FromHexString(expectedHash); }
        catch { return false; }
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>The wire credential is <c>{keyId:N}.{secret}</c>. Returns false on any malformed input.</summary>
    public static bool TryParse(string credential, out Guid keyId, out string secret)
    {
        keyId = default;
        secret = "";
        if (string.IsNullOrWhiteSpace(credential)) return false;
        var dot = credential.IndexOf('.');
        if (dot <= 0 || dot == credential.Length - 1) return false;
        if (!Guid.TryParseExact(credential[..dot], "N", out keyId)) return false;
        secret = credential[(dot + 1)..];
        return secret.Length > 0;
    }

    public static string Format(Guid keyId, string secret) => $"{keyId:N}.{secret}";
}
