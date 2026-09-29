using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;

namespace SmartGym.Domain.Entities.Promotions;

public class PromotionRedemption : BaseEntity
{
    public Guid PromotionId { get; set; }
    public Promotion Promotion { get; set; } = null!;

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public Guid? MembershipId { get; set; }
    public Membership? Membership { get; set; }

    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public int ExtraCreditsGranted { get; set; }

    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;
}
