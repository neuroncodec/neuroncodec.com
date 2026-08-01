using NeuronCodec.Web.Services;

namespace NeuronCodec.Web.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_accepts_the_password_that_was_hashed()
    {
        var hash = PasswordHasher.Hash("Synapse-Decode-2026!");

        Assert.True(PasswordHasher.Verify("Synapse-Decode-2026!", hash));
    }

    [Fact]
    public void Verify_rejects_a_different_password()
    {
        var hash = PasswordHasher.Hash("Synapse-Decode-2026!");

        Assert.False(PasswordHasher.Verify("synapse-decode-2026!", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Hash_salts_each_call_so_equal_passwords_differ()
    {
        var a = PasswordHasher.Hash("same password");
        var b = PasswordHasher.Hash("same password");

        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("same password", a));
        Assert.True(PasswordHasher.Verify("same password", b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2$sha256$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2$sha256$1000$!!!not-base64!!!$aGFzaA==")]
    [InlineData("pbkdf2$sha256$1000$c2FsdA==")]
    public void Verify_returns_false_for_a_malformed_stored_hash(string encoded)
    {
        Assert.False(PasswordHasher.Verify("anything", encoded));
    }

    [Fact]
    public void Hash_records_the_iteration_count_it_used()
    {
        var hash = PasswordHasher.Hash("x");

        Assert.StartsWith("pbkdf2$sha256$210000$", hash);
    }
}

public class PasswordPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_passwords_are_rejected(string? password)
    {
        Assert.Equal("Enter a password.", AdminAuth.ValidatePassword(password));
    }

    [Fact]
    public void Short_passwords_are_rejected()
    {
        Assert.Equal("Use at least 12 characters.", AdminAuth.ValidatePassword("Ab1!short"));
    }

    [Fact]
    public void Passwords_using_fewer_than_three_character_classes_are_rejected()
    {
        Assert.NotNull(AdminAuth.ValidatePassword("alllowercaseletters"));
        Assert.NotNull(AdminAuth.ValidatePassword("lowercaseand1234567"));
    }

    [Theory]
    [InlineData("Synapse-Decode-2026!")]
    [InlineData("correct horse Battery 9")]
    [InlineData("Neuron#Codec#Reads")]
    public void Sufficiently_varied_passwords_are_accepted(string password)
    {
        Assert.Null(AdminAuth.ValidatePassword(password));
    }

    [Fact]
    public void Absurdly_long_passwords_are_rejected()
    {
        Assert.Equal("That password is too long.", AdminAuth.ValidatePassword(new string('a', 300) + "B1!"));
    }
}
