using FluentValidation.TestHelper;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Validators;
using SmartGym.Domain.Enums;
using Xunit;
using static SmartGym.Domain.UnitTests.Promotions.PromotionTestSupport;

namespace SmartGym.Domain.UnitTests.Promotions;

public class PromotionValidatorTests
{
    private readonly CreatePromotionRequestValidator _createValidator = new();
    private readonly UpdatePromotionRequestValidator _updateValidator = new();

    private static UpdatePromotionRequest AsUpdate(CreatePromotionRequest r) => new(
        r.Code, r.Name, r.Description, r.ConditionType, r.BenefitType, r.BenefitValue,
        r.ValidFrom, r.ValidTo, r.MaxUsesTotal, r.MaxUsesPerPerson, r.ApplicablePlanId, Version: 0);

    [Fact]
    public void ValidRequest_HasNoErrors()
    {
        _createValidator.TestValidate(ValidCreateRequest()).ShouldNotHaveAnyValidationErrors();
        _updateValidator.TestValidate(AsUpdate(ValidCreateRequest())).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Code_IsRequired(string code)
    {
        var request = ValidCreateRequest() with { Code = code };

        _createValidator.TestValidate(request).ShouldHaveValidationErrorFor(x => x.Code);
        _updateValidator.TestValidate(AsUpdate(request)).ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public void CodeAndName_RespectMaxLength()
    {
        var request = ValidCreateRequest() with { Code = new string('A', 51), Name = new string('n', 151) };

        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Code);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData(PromotionBenefitType.PercentageDiscount, 0, false)]
    [InlineData(PromotionBenefitType.PercentageDiscount, 100, true)]
    [InlineData(PromotionBenefitType.PercentageDiscount, 150, false)]
    [InlineData(PromotionBenefitType.FixedAmountDiscount, 0, false)]
    [InlineData(PromotionBenefitType.FixedAmountDiscount, 5000, true)]
    [InlineData(PromotionBenefitType.ExtraCredits, 0, false)]
    [InlineData(PromotionBenefitType.ExtraCredits, 1.5, false)]
    [InlineData(PromotionBenefitType.ExtraCredits, 2, true)]
    public void BenefitValue_IsValidatedByBenefitType(PromotionBenefitType type, double value, bool isValid)
    {
        var request = ValidCreateRequest() with { BenefitType = type, BenefitValue = (decimal)value };

        var create = _createValidator.TestValidate(request);
        var update = _updateValidator.TestValidate(AsUpdate(request));

        if (isValid)
        {
            create.ShouldNotHaveValidationErrorFor(x => x.BenefitValue);
            update.ShouldNotHaveValidationErrorFor(x => x.BenefitValue);
        }
        else
        {
            create.ShouldHaveValidationErrorFor(x => x.BenefitValue);
            update.ShouldHaveValidationErrorFor(x => x.BenefitValue);
        }
    }

    [Fact]
    public void ValidTo_CannotPrecedeValidFrom()
    {
        var request = ValidCreateRequest() with { ValidFrom = new DateOnly(2026, 6, 10), ValidTo = new DateOnly(2026, 6, 9) };

        _createValidator.TestValidate(request).ShouldHaveValidationErrorFor(x => x.ValidTo);
        _createValidator.TestValidate(request with { ValidTo = new DateOnly(2026, 6, 10) }).ShouldNotHaveValidationErrorFor(x => x.ValidTo);
        _createValidator.TestValidate(request with { ValidTo = null }).ShouldNotHaveValidationErrorFor(x => x.ValidTo);
    }

    [Fact]
    public void Limits_MustBeAtLeastOne()
    {
        var request = ValidCreateRequest() with { MaxUsesTotal = 0, MaxUsesPerPerson = 0 };

        var create = _createValidator.TestValidate(request);
        create.ShouldHaveValidationErrorFor(x => x.MaxUsesTotal);
        create.ShouldHaveValidationErrorFor(x => x.MaxUsesPerPerson);

        var update = _updateValidator.TestValidate(AsUpdate(request));
        update.ShouldHaveValidationErrorFor(x => x.MaxUsesTotal);
        update.ShouldHaveValidationErrorFor(x => x.MaxUsesPerPerson);

        _createValidator.TestValidate(request with { MaxUsesTotal = null, MaxUsesPerPerson = 1 }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(PromotionConditionType.Referral)]
    [InlineData(PromotionConditionType.TwoForOne)]
    [InlineData((PromotionConditionType)99)]
    public void UnverifiableConditions_AreRejected(PromotionConditionType conditionType)
    {
        var request = ValidCreateRequest() with { ConditionType = conditionType };

        _createValidator.TestValidate(request).ShouldHaveValidationErrorFor(x => x.ConditionType);
        _updateValidator.TestValidate(AsUpdate(request)).ShouldHaveValidationErrorFor(x => x.ConditionType);
    }

    [Theory]
    [InlineData(PromotionConditionType.NewStudent)]
    [InlineData(PromotionConditionType.FamilyGroup)]
    [InlineData(PromotionConditionType.CouponCode)]
    public void VerifiableConditions_AreAccepted(PromotionConditionType conditionType)
    {
        var request = ValidCreateRequest() with { ConditionType = conditionType };

        _createValidator.TestValidate(request).ShouldNotHaveValidationErrorFor(x => x.ConditionType);
    }
}
