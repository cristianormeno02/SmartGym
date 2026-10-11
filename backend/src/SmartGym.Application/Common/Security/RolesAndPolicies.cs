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
}
