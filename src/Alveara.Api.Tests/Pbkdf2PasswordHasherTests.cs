using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

public class Pbkdf2PasswordHasherTests
{
    [Fact]
    public void Hash_never_contains_the_plaintext_password()
    {
        const string password = "correct horse battery staple";
        var hash = Pbkdf2PasswordHasher.Hash(password);

        Assert.DoesNotContain(password, hash);
    }

    [Fact]
    public void Hashing_the_same_password_twice_produces_different_hashes()
    {
        const string password = "correct horse battery staple";
        var hash1 = Pbkdf2PasswordHasher.Hash(password);
        var hash2 = Pbkdf2PasswordHasher.Hash(password);

        Assert.NotEqual(hash1, hash2); // different random salt per call
    }

    [Fact]
    public void Verify_accepts_the_correct_password()
    {
        const string password = "correct horse battery staple";
        var hash = Pbkdf2PasswordHasher.Hash(password);

        Assert.True(Pbkdf2PasswordHasher.Verify(password, hash));
    }

    [Fact]
    public void Verify_rejects_an_incorrect_password()
    {
        var hash = Pbkdf2PasswordHasher.Hash("correct horse battery staple");

        Assert.False(Pbkdf2PasswordHasher.Verify("wrong password", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-the-right-shape")]
    [InlineData("pbkdf2-sha256$not-a-number$c2FsdA==$a2V5")]
    [InlineData("pbkdf2-sha256$210000$not-valid-base64!!!$a2V5")]
    public void Verify_rejects_a_malformed_or_tampered_hash_instead_of_throwing(string malformedHash)
    {
        Assert.False(Pbkdf2PasswordHasher.Verify("any password", malformedHash));
    }
}
