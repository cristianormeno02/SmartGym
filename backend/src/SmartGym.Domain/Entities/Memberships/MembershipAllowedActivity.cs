using SmartGym.Domain.Entities.Activities;

namespace SmartGym.Domain.Entities.Memberships;

public class MembershipAllowedActivity
{
    public Guid MembershipId { get; set; }
    public Membership Membership { get; set; } = null!;

    public Guid ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;
}
