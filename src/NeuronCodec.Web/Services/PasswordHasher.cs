using System.Security.Cryptography;

namespace NeuronCodec.Web.Services;

/// <summary>
/// PBKDF2-HMACSHA256 password hashing. Format: <c>pbkdf2$sha256$&lt;iterations&gt;$&lt;saltB64&gt;$&lt;hashB64&gt;</c>.
/// Used for both the admin password and recovery codes.
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Prefix = "pbkdf2$sha256$";

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        if (string.IsNullOrEmpty(encoded) || !encoded.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = encoded.Split('$');
        // pbkdf2 | sha256 | iterations | salt | hash
        if (parts.Length != 5 || !int.TryParse(parts[2], out var iterations) || iterations <= 0)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
