using SmartGym.Domain.Entities.Identity;

namespace SmartGym.Domain.Entities.Memberships;

public class MembershipSuspension
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MembershipId { get; set; }
    public Membership Membership { get; set; } = null!;

    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? ReactivatedAtDate { get; set; }

    public bool ExtendExpirationDate { get; set; }
    public int ExtensionDaysApplied { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public Guid SuspendedByUserId { get; set; }
    public User? SuspendedByUser { get; set; }
    public DateTime SuspendedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? ReactivatedByUserId { get; set; }
    public User? ReactivatedByUser { get; set; }
    public DateTime? ReactivatedAtUtc { get; set; }

    public bool IsActiveSuspension { get; set; } = true;
}
