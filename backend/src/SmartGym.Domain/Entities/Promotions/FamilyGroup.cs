using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;

namespace SmartGym.Domain.Entities.Promotions;

public class FamilyGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public Guid PrimaryContactId { get; set; }
    public Person PrimaryContact { get; set; } = null!;

    public ICollection<FamilyGroupMember> Members { get; set; } = new List<FamilyGroupMember>();

    public void AddMember(Guid personId, string relationship)
    {
        if (!Members.Any(m => m.PersonId == personId && m.IsActive))
        {
            Members.Add(new FamilyGroupMember
            {
                Id = Guid.NewGuid(),
                FamilyGroupId = Id,
                PersonId = personId,
                Relationship = relationship.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                IsActive = true
            });
        }
    }

    public void RemoveMember(Guid personId)
    {
        var member = Members.FirstOrDefault(m => m.PersonId == personId);
        if (member != null)
        {
            member.IsActive = false;
            member.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public bool HasMember(Guid personId)
    {
        return Members.Any(m => m.PersonId == personId && m.IsActive);
    }
}
