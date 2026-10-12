using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Memberships;

public class MembershipStatusChange
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MembershipId { get; set; }
    public Membership Membership { get; set; } = null!;

    public MembershipStatus? PreviousStatus { get; set; }
    public MembershipStatus NewStatus { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public Guid? ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }

    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
