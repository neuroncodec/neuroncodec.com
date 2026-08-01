using System.Security.Cryptography;
using System.Text;

namespace NeuronCodec.Web.Services;

/// <summary>
/// RFC 6238 TOTP (HMAC-SHA1, 30-second step, 6 digits) — the parameters every mainstream
/// authenticator app assumes by default.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;

    /// <summary>How many steps either side of the current one are accepted, for clock skew.</summary>
    public const int WindowSteps = 1;

    public static string GenerateSecret(int bytes = 20) =>
        Base32.Encode(RandomNumberGenerator.GetBytes(bytes));

    public static long StepFor(DateTimeOffset when) =>
        when.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(string base32Secret, long step)
    {
        var key = Base32.Decode(base32Secret);
        var counter = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(step & 0xff);
            step >>= 8;
        }

        var mac = HMACSHA1.HashData(key, counter);
        var offset = mac[^1] & 0x0f;
        var binary =
            ((mac[offset] & 0x7f) << 24) |
            ((mac[offset + 1] & 0xff) << 16) |
            ((mac[offset + 2] & 0xff) << 8) |
            (mac[offset + 3] & 0xff);

        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString(new string('0', Digits));
    }

    /// <summary>
    /// Validates <paramref name="code"/> against the window around <paramref name="now"/>, rejecting
    /// any step at or below <paramref name="lastUsedStep"/> so a code cannot be replayed.
    /// Returns the matched step so the caller can persist it.
    /// </summary>
    public static bool Validate(string base32Secret, string? code, DateTimeOffset now, long lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;

        var normalized = new string(code.Where(char.IsDigit).ToArray());
        if (normalized.Length != Digits) return false;

        var current = StepFor(now);
        for (var offset = -WindowSteps; offset <= WindowSteps; offset++)
        {
            var step = current + offset;
            if (step <= lastUsedStep) continue;

            var expected = Compute(base32Secret, step);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(normalized)))
            {
                matchedStep = step;
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds the otpauth:// URI an authenticator app scans.</summary>
    public static string BuildUri(string issuer, string account, string base32Secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{account}");
        return $"otpauth://totp/{label}" +
               $"?secret={base32Secret}" +
               $"&issuer={Uri.EscapeDataString(issuer)}" +
               $"&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    /// <summary>Formats a secret in four-character groups for manual entry.</summary>
    public static string FormatForDisplay(string base32Secret) =>
        string.Join(' ', Enumerable.Range(0, (base32Secret.Length + 3) / 4)
            .Select(i => base32Secret.Substring(i * 4, Math.Min(4, base32Secret.Length - i * 4))));
}

/// <summary>RFC 4648 base32, the encoding authenticator apps expect for TOTP secrets.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
            sb.Append(Alphabet[(buffer << (5 - bitsLeft)) & 31]);

        return sb.ToString();
    }

    public static byte[] Decode(string encoded)
    {
        var bytes = new List<byte>(encoded.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;

        foreach (var c in encoded)
        {
            if (c is '=' or ' ' or '-') continue;

            var index = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (index < 0) throw new FormatException($"Invalid base32 character '{c}'.");

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xff));
                bitsLeft -= 8;
            }
        }

        return [.. bytes];
    }
}
