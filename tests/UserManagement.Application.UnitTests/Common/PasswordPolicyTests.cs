using UserManagement.Application.Common;
using UserManagement.Domain.Exceptions;

namespace UserManagement.Application.UnitTests.Common;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Short1!")]        // too short
    [InlineData("alllowercase1!")] // no uppercase
    [InlineData("ALLUPPERCASE1!")] // no lowercase
    [InlineData("NoDigitsHere!")]  // no digit
    [InlineData("NoSymbol1234")]   // no non-alphanumeric
    public void Validate_ReturnsErrors_ForNonCompliantPasswords(string password)
    {
        var errors = PasswordPolicy.Validate(password);

        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("ValidPass1!")]
    [InlineData("Another$ecure9")]
    public void Validate_ReturnsNoErrors_ForCompliantPasswords(string password)
    {
        var errors = PasswordPolicy.Validate(password);

        Assert.Empty(errors);
    }

    [Fact]
    public void EnsureValid_ThrowsValidationException_WhenPasswordIsInvalid()
    {
        var ex = Assert.Throws<ValidationException>(() => PasswordPolicy.EnsureValid("weak", "password"));

        Assert.True(ex.Errors.ContainsKey("password"));
        Assert.NotEmpty(ex.Errors["password"]);
    }

    [Fact]
    public void EnsureValid_DoesNotThrow_WhenPasswordIsValid()
    {
        var exception = Record.Exception(() => PasswordPolicy.EnsureValid("ValidPass1!"));

        Assert.Null(exception);
    }
}
