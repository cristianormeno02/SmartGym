namespace SmartGym.Domain.Enums;

public enum NotificationCategory
{
    Reservations = 1,
    ClassChanges = 2,
    MembershipsAndCredits = 3,
    Payments = 4,
    Promotions = 5,
    Administrative = 6,
    Security = 7
}

public static class NotificationCategoryExtensions
{
    public static bool IsPromotional(this NotificationCategory category) =>
        category == NotificationCategory.Promotions;

    public static bool IsTransactional(this NotificationCategory category) =>
        !category.IsPromotional();
}
