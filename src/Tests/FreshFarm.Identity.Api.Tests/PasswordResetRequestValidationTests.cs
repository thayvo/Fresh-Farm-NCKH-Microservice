using System.ComponentModel.DataAnnotations;
using FreshFarm.Identity.Api.Dtos;
using Xunit;

namespace FreshFarm.Identity.Api.Tests;

public sealed class PasswordResetRequestValidationTests
{
    [Theory]
    [InlineData("short1")]
    [InlineData("onlyletters")]
    [InlineData("12345678")]
    public void ResetPasswordRequest_RejectsPasswordOutsideSharedPolicy(string password)
    {
        var request = new ResetPasswordRequest
        {
            Email = "customer@example.test",
            Token = "valid-test-token",
            NewPassword = password,
            ConfirmPassword = password
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.NewPassword)));
    }

    [Fact]
    public void ResetPasswordRequest_AcceptsPasswordMatchingSharedPolicy()
    {
        var request = new ResetPasswordRequest
        {
            Email = "customer@example.test",
            Token = "valid-test-token",
            NewPassword = "FreshFarm123",
            ConfirmPassword = "FreshFarm123"
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.True(isValid, string.Join("; ", results.Select(result => result.ErrorMessage)));
    }
}
