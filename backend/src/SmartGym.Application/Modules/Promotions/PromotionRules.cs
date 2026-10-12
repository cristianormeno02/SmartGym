using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Promotions;

public static class PromotionRules
{
    /// <summary>
    /// Condiciones que el sistema sabe verificar. El beneficio de las demás nunca se concede: se rechazan
    /// al crear o editar una promoción y al evaluar una ya existente.
    /// </summary>
    public static readonly IReadOnlyCollection<PromotionConditionType> VerifiableConditions =
    [
        PromotionConditionType.NewStudent,
        PromotionConditionType.FamilyGroup,
        PromotionConditionType.CouponCode
    ];

    public static bool IsVerifiable(PromotionConditionType conditionType) =>
        VerifiableConditions.Contains(conditionType);
}
