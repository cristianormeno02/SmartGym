using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Memberships.Dtos;

public static class EligibilityIssueCodes
{
    public const string StudentNotActive = "STUDENT_NOT_ACTIVE";
    public const string MembershipPending = "MEMBERSHIP_PENDING";
    public const string MembershipCancelled = "MEMBERSHIP_CANCELLED";
    public const string MembershipExpired = "MEMBERSHIP_EXPIRED";
    public const string OutOfValidityRange = "OUT_OF_VALIDITY_RANGE";
    public const string MembershipSuspended = "MEMBERSHIP_SUSPENDED";
    public const string ActivityNotActive = "ACTIVITY_NOT_ACTIVE";
    public const string ActivityNotAllowed = "ACTIVITY_NOT_ALLOWED";
    public const string NoAvailableCredits = "NO_AVAILABLE_CREDITS";
    public const string AgeNotAllowed = "AGE_NOT_ALLOWED";
    public const string MedicalCertificateRequired = "MEDICAL_CERTIFICATE_REQUIRED";
    public const string UnderageEmergencyContactRequired = "UNDERAGE_EMERGENCY_CONTACT_REQUIRED";
}

public record EligibilityIssueDto(
    string Code,
    string Message
);

public record MembershipEligibilityDto(
    Guid MembershipId,
    Guid StudentId,
    DateOnly TargetDate,
    Guid? ActivityId,
    Guid? ClassSessionId,
    MembershipStatus Status,
    MembershipStatus EffectiveStatus,
    bool IsUnlimited,
    int AvailableCredits,
    bool IsDepleted,
    bool IsEligible,
    IReadOnlyList<EligibilityIssueDto> Issues
);

public record MembershipSelectionResultDto(
    bool IsEligible,
    Membership? SelectedMembership,
    MembershipEligibilityDto? SelectedEligibility,
    IReadOnlyList<EligibilityIssueDto> Issues
);
