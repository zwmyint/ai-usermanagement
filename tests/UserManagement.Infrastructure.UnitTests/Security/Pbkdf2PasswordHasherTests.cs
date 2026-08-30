using UserManagement.Infrastructure.Security;

namespace UserManagement.Infrastructure.UnitTests.Security;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _sut = new();

    [Fact]
    public void Hash_ProducesDifferentSaltsAndHashes_ForSamePassword()
    {
        var (hash1, salt1) = _sut.Hash("SamePassword1!");
        var (hash2, salt2) = _sut.Hash("SamePassword1!");

        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForCorrectPassword()
    {
        var (hash, salt) = _sut.Hash("CorrectHorseBattery1!");

        Assert.True(_sut.Verify("CorrectHorseBattery1!", hash, salt));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForIncorrectPassword()
    {
        var (hash, salt) = _sut.Hash("CorrectHorseBattery1!");

        Assert.False(_sut.Verify("WrongPassword1!", hash, salt));
    }

    [Theory]
    [InlineData("", "salt")]
    [InlineData("hash", "")]
    [InlineData("not-base64!!", "not-base64!!")]
    public void Verify_ReturnsFalse_ForMalformedInputs(string hash, string salt)
    {
        Assert.False(_sut.Verify("anything", hash, salt));
    }
}
