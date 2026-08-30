using UserManagement.Application.Common;

namespace UserManagement.Application.UnitTests.Common;

public class NormalizerTests
{
    [Theory]
    [InlineData("  Admin@Example.com  ", "ADMIN@EXAMPLE.COM")]
    [InlineData("johndoe", "JOHNDOE")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Normalize_TrimsAndUppercases(string? input, string expected)
    {
        Assert.Equal(expected, Normalizer.Normalize(input));
    }
}
