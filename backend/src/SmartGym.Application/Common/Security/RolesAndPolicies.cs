namespace SmartGym.Application.Common.Security;

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Secretary = "Secretary";
    public const string Instructor = "Instructor";
    public const string Student = "Student";
    public const string Public = "Public";

    public const string Staff = $"{Administrator},{Secretary}";
    public const string TeachingAndStaff = $"{Administrator},{Secretary},{Instructor}";
    public const string AllAuthenticated = $"{Administrator},{Secretary},{Instructor},{Student}";
}

public static class Permissions
{
    public const string MembershipsRead = "MEMBERSHIPS_READ";
    public const string MembershipsCreate = "MEMBERSHIPS_CREATE";
    public const string MembershipsActivate = "MEMBERSHIPS_ACTIVATE";
    public const string MembershipsSuspend = "MEMBERSHIPS_SUSPEND";
    public const string MembershipsReactivate = "MEMBERSHIPS_REACTIVATE";
    public const string MembershipsCancel = "MEMBERSHIPS_CANCEL";
    public const string MembershipsRenew = "MEMBERSHIPS_RENEW";
    public const string MembershipsManage = "MEMBERSHIPS_MANAGE";

    public const string ReservationsRead = "RESERVATIONS_READ";
    public const string ReservationsCreate = "RESERVATIONS_CREATE";
    public const string ReservationsCancel = "RESERVATIONS_CANCEL";
    public const string ReservationsManage = "RESERVATIONS_MANAGE";
    public const string ReservationsWaitlistRead = "RESERVATIONS_WAITLIST_READ";
    public const string ReservationsOverridePolicy = "RESERVATIONS_OVERRIDE_POLICY";

    public const string PromotionsRead = "PROMOTIONS_READ";
    public const string PromotionsManage = "PROMOTIONS_MANAGE";
    public const string PromotionsRedemptionsRead = "PROMOTIONS_REDEMPTIONS_READ";

    public const string PaymentsRead = "PAYMENTS_READ";
    public const string PaymentsCreate = "PAYMENTS_CREATE";
    public const string PaymentsConfirm = "PAYMENTS_CONFIRM";
    public const string PaymentsCancel = "PAYMENTS_CANCEL";
    public const string PaymentsAllocate = "PAYMENTS_ALLOCATE";
    public const string PaymentsRefund = "PAYMENTS_REFUND";
    public const string PaymentsReconcile = "PAYMENTS_RECONCILE";

    public const string NotificationsReadOwn = "NOTIFICATIONS_READ_OWN";
    public const string NotificationsMarkReadOwn = "NOTIFICATIONS_MARK_READ_OWN";
    public const string NotificationsPreferencesManage = "NOTIFICATIONS_PREFERENCES_MANAGE";
    public const string NotificationsReadAll = "NOTIFICATIONS_READ_ALL";
    public const string NotificationsManage = "NOTIFICATIONS_MANAGE";
    public const string NotificationsDeliveryRead = "NOTIFICATIONS_DELIVERY_READ";
    public const string NotificationsRetryFailed = "NOTIFICATIONS_RETRY_FAILED";
    public const string NotificationsTemplatesRead = "NOTIFICATIONS_TEMPLATES_READ";
    public const string NotificationsTemplatesManage = "NOTIFICATIONS_TEMPLATES_MANAGE";
}

public static class Policies
{
    public const string RequireAdministrator = "RequireAdministrator";
    public const string RequireSecretary = "RequireSecretary";
    public const string RequireInstructor = "RequireInstructor";
    public const string RequireStudent = "RequireStudent";
    public const string RequireStaff = "RequireStaff";
    public const string RequireTeachingAndStaff = "RequireTeachingAndStaff";
    public const string RequireAllAuthenticated = "RequireAllAuthenticated";

    public const string MembershipsRead = Permissions.MembershipsRead;
    public const string MembershipsCreate = Permissions.MembershipsCreate;
    public const string MembershipsActivate = Permissions.MembershipsActivate;
    public const string MembershipsSuspend = Permissions.MembershipsSuspend;
    public const string MembershipsReactivate = Permissions.MembershipsReactivate;
    public const string MembershipsCancel = Permissions.MembershipsCancel;
    public const string MembershipsRenew = Permissions.MembershipsRenew;
    public const string MembershipsManage = Permissions.MembershipsManage;

    public const string ReservationsRead = Permissions.ReservationsRead;
    public const string ReservationsCreate = Permissions.ReservationsCreate;
    public const string ReservationsCancel = Permissions.ReservationsCancel;
    public const string ReservationsManage = Permissions.ReservationsManage;
    public const string ReservationsWaitlistRead = Permissions.ReservationsWaitlistRead;
    public const string ReservationsOverridePolicy = Permissions.ReservationsOverridePolicy;

    public const string PromotionsRead = Permissions.PromotionsRead;
    public const string PromotionsManage = Permissions.PromotionsManage;
    public const string PromotionsRedemptionsRead = Permissions.PromotionsRedemptionsRead;

    public const string PaymentsRead = Permissions.PaymentsRead;
    public const string PaymentsCreate = Permissions.PaymentsCreate;
    public const string PaymentsConfirm = Permissions.PaymentsConfirm;
    public const string PaymentsCancel = Permissions.PaymentsCancel;
    public const string PaymentsAllocate = Permissions.PaymentsAllocate;
    public const string PaymentsRefund = Permissions.PaymentsRefund;
    public const string PaymentsReconcile = Permissions.PaymentsReconcile;

    public const string NotificationsReadOwn = Permissions.NotificationsReadOwn;
    public const string NotificationsMarkReadOwn = Permissions.NotificationsMarkReadOwn;
    public const string NotificationsPreferencesManage = Permissions.NotificationsPreferencesManage;
    public const string NotificationsReadAll = Permissions.NotificationsReadAll;
    public const string NotificationsManage = Permissions.NotificationsManage;
    public const string NotificationsDeliveryRead = Permissions.NotificationsDeliveryRead;
    public const string NotificationsRetryFailed = Permissions.NotificationsRetryFailed;
    public const string NotificationsTemplatesRead = Permissions.NotificationsTemplatesRead;
    public const string NotificationsTemplatesManage = Permissions.NotificationsTemplatesManage;
}
