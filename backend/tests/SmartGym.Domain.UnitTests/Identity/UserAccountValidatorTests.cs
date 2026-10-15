using FluentValidation.TestHelper;
using SmartGym.Application.Modules.Identity.Dtos;
using SmartGym.Application.Modules.Identity.Validators;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class UserAccountValidatorTests
{
    private readonly ProvisionUserAccountRequestValidator _provisionValidator = new();
    private readonly UpdateUserRolesRequestValidator _updateRolesValidator = new();
    private readonly AdminResetPasswordRequestValidator _resetPasswordValidator = new();
    private readonly UpdateUserStatusRequestValidator _statusValidator = new();

    [Theory]
    [InlineData("Password123!")]
    [InlineData("Admin.2026!")]
    [InlineData("Super#Secret99")]
    public void ProvisionUser_ValidPasswordAndRoles_ShouldPass(string password)
    {
        var request = new ProvisionUserAccountRequest(password, new List<int> { 1, 2 });
        var result = _provisionValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Pass1!")] // < 8 chars
    [InlineData("password123!")] // no uppercase
    [InlineData("Password!!!")] // no digit
    [InlineData("Password123")] // no symbol
    public void ProvisionUser_InvalidPassword_ShouldFail(string password)
    {
        var request = new ProvisionUserAccountRequest(password, new List<int> { 2 });
        var result = _provisionValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void ProvisionUser_NullOrEmptyRoles_ShouldPass()
    {
        var request = new ProvisionUserAccountRequest("Password123!", null);
        var result = _provisionValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();

        var request2 = new ProvisionUserAccountRequest("Password123!", new List<int>());
        var result2 = _provisionValidator.TestValidate(request2);
        result2.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)] // RoleType.Public is 5, not assignable
    [InlineData(-1)]
    [InlineData(99)]
    public void ProvisionUser_InvalidRoleId_ShouldFail(int invalidRoleId)
    {
        var request = new ProvisionUserAccountRequest("Password123!", new List<int> { 1, invalidRoleId });
        var result = _provisionValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.RoleIds);
    }

    [Fact]
    public void UpdateRoles_ValidRoles_ShouldPass()
    {
        var request = new UpdateUserRolesRequest(new List<int> { 1, 3, 4 });
        var result = _updateRolesValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateRoles_EmptyOrNullRoles_ShouldPass()
    {
        var request = new UpdateUserRolesRequest(null);
        var result = _updateRolesValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();

        var request2 = new UpdateUserRolesRequest(new List<int>());
        var result2 = _updateRolesValidator.TestValidate(request2);
        result2.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    public void UpdateRoles_InvalidRoleId_ShouldFail(int invalidRoleId)
    {
        var request = new UpdateUserRolesRequest(new List<int> { 2, invalidRoleId });
        var result = _updateRolesValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.RoleIds);
    }

    [Theory]
    [InlineData("Password123!")]
    [InlineData("NewSecure#1")]
    public void ResetPassword_ValidPassword_ShouldPass(string password)
    {
        var request = new AdminResetPasswordRequest(password);
        var result = _resetPasswordValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Short1!")]
    [InlineData("nouppercase123!")]
    [InlineData("PasswordNoDigits!")]
    [InlineData("PasswordNoSymbols1")]
    public void ResetPassword_InvalidPassword_ShouldFail(string password)
    {
        var request = new AdminResetPasswordRequest(password);
        var result = _resetPasswordValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateStatus_ValidStatus_ShouldPass(bool isActive)
    {
        var request = new UpdateUserStatusRequest(isActive);
        var result = _statusValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
