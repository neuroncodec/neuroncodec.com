using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Tests;

public class TotpTests
{
    // RFC 6238 test vectors. The published values use the ASCII secret "12345678901234567890",
    // which is this string once base32-encoded.
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1111111111, "050471")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void Compute_matches_the_rfc_6238_vectors(long unixSeconds, string expected)
    {
        var step = unixSeconds / Totp.StepSeconds;

        Assert.Equal(expected, Totp.Compute(RfcSecret, step));
    }

    [Fact]
    public void Validate_accepts_the_current_code()
    {
        var secret = Totp.GenerateSecret();
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000);
        var code = Totp.Compute(secret, Totp.StepFor(now));

        Assert.True(Totp.Validate(secret, code, now, lastUsedStep: 0, out var matched));
        Assert.Equal(Totp.StepFor(now), matched);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(30)]
    public void Validate_tolerates_one_step_of_clock_skew(int offsetSeconds)
    {
        var secret = Totp.GenerateSecret();
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000);
        var skewed = now.AddSeconds(offsetSeconds);
        var code = Totp.Compute(secret, Totp.StepFor(skewed));

        Assert.True(Totp.Validate(secret, code, now, lastUsedStep: 0, out _));
    }

    [Fact]
    public void Validate_rejects_a_code_two_steps_away()
    {
        var secret = Totp.GenerateSecret();
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000);
        var code = Totp.Compute(secret, Totp.StepFor(now.AddSeconds(90)));

        Assert.False(Totp.Validate(secret, code, now, lastUsedStep: 0, out _));
    }

    [Fact]
    public void Validate_rejects_a_replay_of_an_already_used_step()
    {
        var secret = Totp.GenerateSecret();
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000);
        var step = Totp.StepFor(now);
        var code = Totp.Compute(secret, step);

        Assert.True(Totp.Validate(secret, code, now, lastUsedStep: 0, out var matched));
        // Second use of the same step, exactly as AdminAuth would record it.
        Assert.False(Totp.Validate(secret, code, now, lastUsedStep: matched, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    public void Validate_rejects_malformed_input(string? code)
    {
        var secret = Totp.GenerateSecret();

        Assert.False(Totp.Validate(secret, code, DateTimeOffset.UnixEpoch, lastUsedStep: 0, out _));
    }

    [Fact]
    public void GenerateSecret_produces_a_decodable_160_bit_key()
    {
        var secret = Totp.GenerateSecret();

        Assert.Equal(32, secret.Length);
        Assert.Equal(20, Base32.Decode(secret).Length);
    }

    [Fact]
    public void BuildUri_carries_the_parameters_an_authenticator_expects()
    {
        var uri = Totp.BuildUri("NeuronCodec", "admin", RfcSecret);

        Assert.StartsWith("otpauth://totp/NeuronCodec%3Aadmin?", uri);
        Assert.Contains($"secret={RfcSecret}", uri);
        Assert.Contains("issuer=NeuronCodec", uri);
        Assert.Contains("algorithm=SHA1", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }
}

public class Base32Tests
{
    // RFC 4648 section 10 test vectors.
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Encode_matches_rfc_4648(string input, string expected)
    {
        Assert.Equal(expected, Base32.Encode(System.Text.Encoding.ASCII.GetBytes(input)));
    }

    [Fact]
    public void Decode_round_trips_encode()
    {
        var data = new byte[64];
        Random.Shared.NextBytes(data);

        Assert.Equal(data, Base32.Decode(Base32.Encode(data)));
    }

    [Fact]
    public void Decode_ignores_the_spacing_used_for_manual_entry()
    {
        var expected = Base32.Decode("MZXW6YTBOI");

        Assert.Equal(expected, Base32.Decode("MZXW 6YTB OI"));
        Assert.Equal(expected, Base32.Decode("mzxw6ytboi"));
    }

    [Fact]
    public void Decode_rejects_characters_outside_the_alphabet()
    {
        Assert.Throws<FormatException>(() => Base32.Decode("MZXW6YTB01"));
    }
}
