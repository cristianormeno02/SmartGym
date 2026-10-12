using FluentValidation;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Validators;

public class RegisterPaymentRequestValidator : AbstractValidator<RegisterPaymentRequest>
{
    public RegisterPaymentRequestValidator()
    {
        RuleFor(x => x.PayerPersonId).NotEmpty().WithMessage("La persona que realiza el pago es obligatoria.");
        RuleFor(x => x.Amount).ValidMoneyAmount();
        RuleFor(x => x.Currency).ValidCurrencyCode();
        RuleFor(x => x.Method).IsInEnum().WithMessage("El medio de pago no es válido.");
        RuleFor(x => x.Status)
            .Must(s => s is PaymentStatus.Pending or PaymentStatus.Completed)
            .WithMessage("El estado inicial del pago debe ser Completed o Pending.");
        RuleFor(x => x.ClassSessionId)
            .Null().When(x => x.MembershipId.HasValue)
            .WithMessage("Un pago no puede aplicarse a una membresía y a una clase a la vez.");
        RuleFor(x => x.ExternalTransactionId)
            .MaximumLength(150).WithMessage("La referencia externa no puede superar los 150 caracteres.");
        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Las notas no pueden superar los 500 caracteres.");
    }
}

public class ApplyPaymentRequestValidator : AbstractValidator<ApplyPaymentRequest>
{
    public ApplyPaymentRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.MembershipId.HasValue != x.ClassSessionId.HasValue)
            .WithName("Obligation")
            .WithMessage("Debe indicarse exactamente una membresía o una clase.");
    }
}

public class FailPaymentRequestValidator : AbstractValidator<FailPaymentRequest>
{
    public FailPaymentRequestValidator()
    {
        RuleFor(x => x.Reason).ValidReason("El motivo de rechazo del pago es obligatorio.");
    }
}

public class CancelPaymentRequestValidator : AbstractValidator<CancelPaymentRequest>
{
    public CancelPaymentRequestValidator()
    {
        RuleFor(x => x.Reason).ValidReason("El motivo de cancelación del pago es obligatorio.");
    }
}

public class CreatePaymentRefundRequestValidator : AbstractValidator<CreatePaymentRefundRequest>
{
    public CreatePaymentRefundRequestValidator()
    {
        RuleFor(x => x.Amount).ValidMoneyAmount();
        RuleFor(x => x.Reason).ValidReason("El motivo de la devolución es obligatorio.");
        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Las notas no pueden superar los 500 caracteres.");
    }
}

internal static class PaymentValidationRules
{
    public static IRuleBuilderOptions<T, decimal> ValidMoneyAmount<T>(this IRuleBuilder<T, decimal> rule) =>
        rule
            .GreaterThan(0).WithMessage("El importe debe ser superior a cero.")
            .Must(amount => decimal.Round(amount, 2) == amount)
            .WithMessage("El importe admite a lo sumo dos decimales.");

    public static IRuleBuilderOptions<T, string?> ValidCurrencyCode<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .Must(c => c == null || (c.Length == 3 && c.All(char.IsAsciiLetter)))
            .WithMessage("La moneda debe ser un código ISO 4217 de 3 letras.");

    public static IRuleBuilderOptions<T, string> ValidReason<T>(this IRuleBuilder<T, string> rule, string requiredMessage) =>
        rule
            .NotEmpty().WithMessage(requiredMessage)
            .MaximumLength(500).WithMessage("El motivo no puede superar los 500 caracteres.");
}
