using SmartGym.Domain.Entities.Activities;

namespace SmartGym.Domain.Entities.Memberships;

public class MembershipPlanActivity
{
    public Guid MembershipPlanId { get; set; }
    public MembershipPlan MembershipPlan { get; set; } = null!;

    public Guid ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;
}
