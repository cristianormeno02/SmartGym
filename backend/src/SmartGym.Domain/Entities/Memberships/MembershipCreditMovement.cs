using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Memberships;

public class MembershipCreditMovement : BaseEntity
{
    public Guid? MembershipId { get; set; }
    public Membership? Membership { get; set; }

    public Guid StudentId { get; set; }
    public Person Student { get; set; } = null!;

    public Guid? ClassSessionId { get; set; }
    public ClassSession? ClassSession { get; set; }

    public CreditMovementType Type { get; set; }
    public int Amount { get; set; }
    public int BalanceAfter { get; set; }
    public string Reason { get; set; } = string.Empty;

    public Guid? PerformedByUserId { get; set; }
}
