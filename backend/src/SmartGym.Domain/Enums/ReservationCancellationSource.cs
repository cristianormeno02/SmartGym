namespace SmartGym.Domain.Enums;

public enum ReservationCancellationSource
{
    Student = 1,
    Staff = 2,
    ClassSessionChange = 3,
    MembershipChange = 4,
    WaitlistOfferExpired = 5,
    WaitlistDeclined = 6,
    WaitlistExpired = 7,
    AttendanceRevoked = 8
}
