using System.Security.Cryptography;
using System.Text;

namespace ElderSafe.Api.Security;

/// <summary>
/// Generates and hashes device keys.
/// </summary>
/// <remarks>
/// <para>
/// A plain SHA-256, deliberately, where a password would use a slow algorithm
/// such as PBKDF2. Slow hashing exists because people choose guessable
/// passwords; a key generated here is 256 random bits, which cannot be
/// guessed however fast an attacker hashes. Using a slow hash instead would
/// cost every reading a key derivation for no security gained.
/// </para>
/// <para>
/// A deterministic hash also lets the authentication handler find the device
/// by a single indexed lookup rather than hashing against every stored device
/// in turn.
/// </para>
/// </remarks>
public static class DeviceKey
{
    /// <summary>Creates a new key. Shown once, at pairing, and never stored.</summary>
    public static string Generate()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    /// <summary>Hashes a key for storage and lookup.</summary>
    public static string Hash(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
}
