namespace SmartGym.Domain.Enums;

public enum NotificationType
{
    ClassSuspended = 1,
    ClassReminder = 2,
    MembershipExpiring = 3,
    PromotionAvailable = 4,
    PaymentConfirmed = 5,
    MembershipActivated = 6,
    MembershipSuspended = 7,
    MembershipExpired = 8,
    ReservationConfirmed = 9,
    ReservationCancelled = 10,
    ReservationWaitlisted = 11,
    ReservationPromotedFromWaitlist = 12,
    WaitlistSpotOffered = 13,
    WaitlistOfferExpired = 14,
    ClassCancelled = 15,
    ClassRescheduled = 16,
    ClassRoomChanged = 17,
    ClassInstructorChanged = 18,
    MembershipReactivated = 19,
    CreditsCompensated = 20,
    PaymentFailed = 21,
    PaymentRefunded = 22,
    AdministrativeNotice = 23,
    SecurityAlert = 24
}

public static class NotificationTypeExtensions
{
    public static NotificationCategory GetCategory(this NotificationType type) =>
        type switch
        {
            NotificationType.ClassSuspended
                or NotificationType.ClassCancelled
                or NotificationType.ClassRescheduled
                or NotificationType.ClassRoomChanged
                or NotificationType.ClassInstructorChanged => NotificationCategory.ClassChanges,

            NotificationType.ClassReminder
                or NotificationType.ReservationConfirmed
                or NotificationType.ReservationCancelled
                or NotificationType.ReservationWaitlisted
                or NotificationType.ReservationPromotedFromWaitlist
                or NotificationType.WaitlistSpotOffered
                or NotificationType.WaitlistOfferExpired => NotificationCategory.Reservations,

            NotificationType.MembershipExpiring
                or NotificationType.MembershipActivated
                or NotificationType.MembershipSuspended
                or NotificationType.MembershipExpired
                or NotificationType.MembershipReactivated
                or NotificationType.CreditsCompensated => NotificationCategory.MembershipsAndCredits,

            NotificationType.PaymentConfirmed
                or NotificationType.PaymentFailed
                or NotificationType.PaymentRefunded => NotificationCategory.Payments,

            NotificationType.PromotionAvailable => NotificationCategory.Promotions,
            NotificationType.AdministrativeNotice => NotificationCategory.Administrative,
            NotificationType.SecurityAlert => NotificationCategory.Security,
            _ => NotificationCategory.Administrative
        };

    public static string GetDefaultActionUrl(this NotificationType type) =>
        type switch
        {
            NotificationType.PromotionAvailable => "/planes",
            NotificationType.AdministrativeNotice => "/portal/notificaciones",
            NotificationType.SecurityAlert => "/portal/mis-datos",
            _ => "/portal/alumno"
        };

    public static string GetDefaultRelatedEntityType(this NotificationType type) =>
        type switch
        {
            NotificationType.ClassSuspended
                or NotificationType.ClassCancelled
                or NotificationType.ClassRescheduled
                or NotificationType.ClassRoomChanged
                or NotificationType.ClassInstructorChanged => "ClassSession",

            NotificationType.ClassReminder
                or NotificationType.ReservationConfirmed
                or NotificationType.ReservationCancelled
                or NotificationType.ReservationWaitlisted
                or NotificationType.ReservationPromotedFromWaitlist
                or NotificationType.WaitlistSpotOffered
                or NotificationType.WaitlistOfferExpired => "Reservation",

            NotificationType.MembershipExpiring
                or NotificationType.MembershipActivated
                or NotificationType.MembershipSuspended
                or NotificationType.MembershipExpired
                or NotificationType.MembershipReactivated
                or NotificationType.CreditsCompensated => "Membership",

            NotificationType.PaymentConfirmed
                or NotificationType.PaymentFailed
                or NotificationType.PaymentRefunded => "Payment",

            NotificationType.PromotionAvailable => "Promotion",
            NotificationType.SecurityAlert => "User",
            _ => "Person"
        };
}
