using FluentValidation;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Promotions.Validators;

public class CreatePromotionRequestValidator : AbstractValidator<CreatePromotionRequest>
{
    public CreatePromotionRequestValidator()
    {
        RuleFor(x => x.Code).ValidPromotionCode();
        RuleFor(x => x.Name).ValidPromotionName();
        RuleFor(x => x.Description).ValidPromotionDescription();
        RuleFor(x => x.ConditionType).VerifiableCondition();
        RuleFor(x => x.BenefitType).IsInEnum().WithMessage("El tipo de beneficio no es válido.");
        RuleFor(x => x.BenefitValue).ValidBenefitValue(x => x.BenefitType);
        RuleFor(x => x.ValidTo).NotBefore(x => x.ValidFrom);
        RuleFor(x => x.MaxUsesTotal).ValidTotalLimit();
        RuleFor(x => x.MaxUsesPerPerson).ValidPerPersonLimit();
    }
}

public class UpdatePromotionRequestValidator : AbstractValidator<UpdatePromotionRequest>
{
    public UpdatePromotionRequestValidator()
    {
        RuleFor(x => x.Code).ValidPromotionCode();
        RuleFor(x => x.Name).ValidPromotionName();
        RuleFor(x => x.Description).ValidPromotionDescription();
        RuleFor(x => x.ConditionType).VerifiableCondition();
        RuleFor(x => x.BenefitType).IsInEnum().WithMessage("El tipo de beneficio no es válido.");
        RuleFor(x => x.BenefitValue).ValidBenefitValue(x => x.BenefitType);
        RuleFor(x => x.ValidTo).NotBefore(x => x.ValidFrom);
        RuleFor(x => x.MaxUsesTotal).ValidTotalLimit();
        RuleFor(x => x.MaxUsesPerPerson).ValidPerPersonLimit();
    }
}

internal static class PromotionValidationRules
{
    public static IRuleBuilderOptions<T, string> ValidPromotionCode<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("El código de la promoción es obligatorio.")
            .MaximumLength(50).WithMessage("El código no puede superar los 50 caracteres.");

    public static IRuleBuilderOptions<T, string> ValidPromotionName<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("El nombre de la promoción es obligatorio.")
            .MaximumLength(150).WithMessage("El nombre no puede superar los 150 caracteres.");

    public static IRuleBuilderOptions<T, string?> ValidPromotionDescription<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(1000).WithMessage("La descripción no puede superar los 1000 caracteres.");

    public static IRuleBuilderOptions<T, PromotionConditionType> VerifiableCondition<T>(
        this IRuleBuilder<T, PromotionConditionType> rule) =>
        rule
            .Must(PromotionRules.IsVerifiable)
            .WithMessage("Este tipo de condición todavía no está disponible.");

    public static IRuleBuilderOptions<T, decimal> ValidBenefitValue<T>(
        this IRuleBuilder<T, decimal> rule,
        Func<T, PromotionBenefitType> benefitType) =>
        rule
            .Must((request, value) => benefitType(request) switch
            {
                PromotionBenefitType.PercentageDiscount => value > 0m && value <= 100m,
                PromotionBenefitType.FixedAmountDiscount => value > 0m,
                PromotionBenefitType.ExtraCredits => value >= 1m && value == decimal.Truncate(value),
                _ => true
            })
            .WithMessage((request, _) => benefitType(request) switch
            {
                PromotionBenefitType.PercentageDiscount => "El descuento porcentual debe ser mayor que 0 y menor o igual que 100.",
                PromotionBenefitType.FixedAmountDiscount => "El descuento fijo debe ser mayor que 0.",
                _ => "Los créditos adicionales deben ser un número entero mayor o igual que 1."
            });

    public static IRuleBuilderOptions<T, DateOnly?> NotBefore<T>(
        this IRuleBuilder<T, DateOnly?> rule,
        Func<T, DateOnly> validFrom) =>
        rule
            .Must((request, validTo) => !validTo.HasValue || validTo.Value >= validFrom(request))
            .WithMessage("La fecha de fin no puede ser anterior a la fecha de inicio.");

    public static IRuleBuilderOptions<T, int?> ValidTotalLimit<T>(this IRuleBuilder<T, int?> rule) =>
        rule
            .Must(value => !value.HasValue || value.Value >= 1)
            .WithMessage("El tope general debe ser mayor o igual que 1.");

    public static IRuleBuilderOptions<T, int> ValidPerPersonLimit<T>(this IRuleBuilder<T, int> rule) =>
        rule
            .GreaterThanOrEqualTo(1).WithMessage("El tope por persona debe ser mayor o igual que 1.");
}
