using System.Text.RegularExpressions;
using FluentValidation;
using SmartGym.Application.Modules.Identity.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Identity.Validators;

public static class PasswordPolicyValidator
{
    private static readonly Regex PasswordRegex = new(
        @"^(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).{8,}$",
        RegexOptions.Compiled);

    public const string ErrorMessage =
        "La contraseña debe tener al menos 8 caracteres, al menos una letra mayúscula, un número y un carácter especial.";

    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithMessage("La contraseña es obligatoria.")
            .MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .Must(p => !string.IsNullOrEmpty(p) && PasswordRegex.IsMatch(p))
            .WithMessage(ErrorMessage);
    }

    public static bool IsAssignableRole(int roleId)
    {
        return roleId is >= (int)RoleType.Administrator and <= (int)RoleType.Student;
    }
}

public class ProvisionUserAccountRequestValidator : AbstractValidator<ProvisionUserAccountRequest>
{
    public ProvisionUserAccountRequestValidator()
    {
        RuleFor(x => x.Password)
            .ValidPassword();

        RuleFor(x => x.RoleIds)
            .Must(roles => roles == null || roles.All(PasswordPolicyValidator.IsAssignableRole))
            .WithMessage("Los roles asignables sólo pueden ser Administrador (1), Secretaria (2), Profesor (3) o Alumno (4).");
    }
}

public class UpdateUserRolesRequestValidator : AbstractValidator<UpdateUserRolesRequest>
{
    public UpdateUserRolesRequestValidator()
    {
        RuleFor(x => x.RoleIds)
            .Must(roles => roles == null || roles.All(PasswordPolicyValidator.IsAssignableRole))
            .WithMessage("Los roles asignables sólo pueden ser Administrador (1), Secretaria (2), Profesor (3) o Alumno (4).");
    }
}

public class UpdateUserStatusRequestValidator : AbstractValidator<UpdateUserStatusRequest>
{
    public UpdateUserStatusRequestValidator()
    {
    }
}

public class AdminResetPasswordRequestValidator : AbstractValidator<AdminResetPasswordRequest>
{
    public AdminResetPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword)
            .ValidPassword();
    }
}
