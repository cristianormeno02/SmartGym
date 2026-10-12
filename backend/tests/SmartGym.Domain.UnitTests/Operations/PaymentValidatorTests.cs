using FluentValidation.TestHelper;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Validators;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentValidatorTests
{
    private static RegisterPaymentRequest ValidRegister() => new(
        PayerPersonId: Guid.NewGuid(),
        MembershipId: Guid.NewGuid(),
        ClassSessionId: null,
        Amount: 25000.50m,
        Method: PaymentMethod.Cash,
        ExternalTransactionId: null,
        Notes: null);

    [Fact]
    public void RegisterPayment_ValidRequest_ShouldPass()
    {
        var result = new RegisterPaymentRequestValidator().TestValidate(ValidRegister() with { Currency = "ARS" });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(100.001)]
    public void RegisterPayment_InvalidAmount_ShouldFail(decimal amount)
    {
        var result = new RegisterPaymentRequestValidator().TestValidate(ValidRegister() with { Amount = amount });

        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Theory]
    [InlineData("AR")]
    [InlineData("PESOS")]
    [InlineData("A1S")]
    public void RegisterPayment_InvalidCurrency_ShouldFail(string currency)
    {
        var result = new RegisterPaymentRequestValidator().TestValidate(ValidRegister() with { Currency = currency });

        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void RegisterPayment_WithMembershipAndClassSession_ShouldFail()
    {
        var result = new RegisterPaymentRequestValidator().TestValidate(
            ValidRegister() with { ClassSessionId = Guid.NewGuid() });

        result.ShouldHaveValidationErrorFor(x => x.ClassSessionId);
    }

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    public void RegisterPayment_InitialStatusOtherThanPendingOrCompleted_ShouldFail(PaymentStatus status)
    {
        var result = new RegisterPaymentRequestValidator().TestValidate(ValidRegister() with { Status = status });

        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void RegisterPayment_InvalidMethodOrLongTexts_ShouldFail()
    {
        var result = new RegisterPaymentRequestValidator().TestValidate(ValidRegister() with
        {
            Method = (PaymentMethod)99,
            ExternalTransactionId = new string('x', 151),
            Notes = new string('x', 501)
        });

        result.ShouldHaveValidationErrorFor(x => x.Method);
        result.ShouldHaveValidationErrorFor(x => x.ExternalTransactionId);
        result.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void ApplyPayment_ExactlyOneObligation_IsRequired()
    {
        var validator = new ApplyPaymentRequestValidator();

        validator.TestValidate(new ApplyPaymentRequest(Guid.NewGuid(), null)).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new ApplyPaymentRequest(null, Guid.NewGuid())).ShouldNotHaveAnyValidationErrors();
        Assert.False(validator.TestValidate(new ApplyPaymentRequest(null, null)).IsValid);
        Assert.False(validator.TestValidate(new ApplyPaymentRequest(Guid.NewGuid(), Guid.NewGuid())).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FailAndCancel_WithoutReason_ShouldFail(string? reason)
    {
        new FailPaymentRequestValidator().TestValidate(new FailPaymentRequest(reason!))
            .ShouldHaveValidationErrorFor(x => x.Reason);
        new CancelPaymentRequestValidator().TestValidate(new CancelPaymentRequest(reason!))
            .ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void FailAndCancel_WithReason_ShouldPass()
    {
        new FailPaymentRequestValidator().TestValidate(new FailPaymentRequest("Comprobante inválido"))
            .ShouldNotHaveAnyValidationErrors();
        new CancelPaymentRequestValidator().TestValidate(new CancelPaymentRequest("El alumno desistió"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateRefund_ValidRequest_ShouldPass()
    {
        new CreatePaymentRefundRequestValidator()
            .TestValidate(new CreatePaymentRefundRequest(10000m, "Descuento comercial post-venta"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.555)]
    public void CreateRefund_InvalidAmount_ShouldFail(decimal amount)
    {
        new CreatePaymentRefundRequestValidator()
            .TestValidate(new CreatePaymentRefundRequest(amount, "Motivo"))
            .ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void CreateRefund_WithoutReasonOrTooLong_ShouldFail()
    {
        var validator = new CreatePaymentRefundRequestValidator();

        validator.TestValidate(new CreatePaymentRefundRequest(100m, " ")).ShouldHaveValidationErrorFor(x => x.Reason);
        validator.TestValidate(new CreatePaymentRefundRequest(100m, new string('x', 501))).ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
