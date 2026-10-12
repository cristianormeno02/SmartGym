namespace SmartGym.Application.Modules.Promotions;

public static class PromotionErrorCodes
{
    public const string NotFound = "PROMOTION_NOT_FOUND";
    public const string NotApplicable = "PROMOTION_NOT_APPLICABLE";
    public const string LimitReached = "PROMOTION_LIMIT_REACHED";
    public const string CodeTaken = "PROMOTION_CODE_TAKEN";
    public const string InvalidTransition = "PROMOTION_INVALID_TRANSITION";
    public const string LockedFields = "PROMOTION_LOCKED_FIELDS";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
}
