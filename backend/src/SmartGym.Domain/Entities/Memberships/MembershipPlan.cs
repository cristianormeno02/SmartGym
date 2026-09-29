using SmartGym.Domain.Common;

namespace SmartGym.Domain.Entities.Memberships;

public class MembershipPlan : BaseEntity
{
    private decimal _price;
    private int _durationDays = 30;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int DurationDays
    {
        get => _durationDays;
        set
        {
            if (value < 1)
            {
                throw new ArgumentException("La duración del plan debe ser de al menos 1 día.", nameof(value));
            }
            _durationDays = value;
        }
    }

    public decimal Price
    {
        get => _price;
        set
        {
            if (value < 0)
            {
                throw new ArgumentException("El precio no puede ser negativo.", nameof(value));
            }
            _price = value;
        }
    }

    public int Credits { get; set; } = 8;
    public bool IsUnlimited { get; set; } = false;
    public bool AppliesToAllActivities { get; set; } = true;

    public ICollection<MembershipPlanActivity> AllowedActivities { get; set; } = new List<MembershipPlanActivity>();

    public void AddAllowedActivity(Guid activityId)
    {
        if (!AllowedActivities.Any(a => a.ActivityId == activityId))
        {
            AllowedActivities.Add(new MembershipPlanActivity
            {
                MembershipPlanId = Id,
                ActivityId = activityId
            });
        }
    }

    public void RemoveAllowedActivity(Guid activityId)
    {
        var existing = AllowedActivities.FirstOrDefault(a => a.ActivityId == activityId);
        if (existing != null)
        {
            AllowedActivities.Remove(existing);
        }
    }

    public bool AllowsActivity(Guid activityId)
    {
        if (AppliesToAllActivities) return true;
        return AllowedActivities.Any(a => a.ActivityId == activityId);
    }
}
