using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;

namespace SmartGym.Domain.Entities.Promotions;

public class FamilyGroupMember : BaseEntity
{
    public Guid FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = null!;

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public string Relationship { get; set; } = string.Empty;
}
