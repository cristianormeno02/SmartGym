using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Activities;

public class Activity : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public int MinCapacity { get; set; } = 1;
    public int MaxCapacity { get; set; } = 30;
    public string? LogoUrl { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public ActivityStatus Status { get; set; } = ActivityStatus.Enabled;
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }

    public Guid? DefaultRoomId { get; set; }
    public Room? DefaultRoom { get; set; }

    public void SetCapacities(int minCapacity, int maxCapacity)
    {
        if (minCapacity < 1)
        {
            throw new ArgumentException("La capacidad mínima no puede ser menor a 1.", nameof(minCapacity));
        }

        if (maxCapacity < minCapacity)
        {
            throw new ArgumentException("La capacidad máxima no puede ser inferior a la capacidad mínima.", nameof(maxCapacity));
        }

        MinCapacity = minCapacity;
        MaxCapacity = maxCapacity;
    }

    public bool IsAgeAllowed(int age)
    {
        if (MinAge.HasValue && age < MinAge.Value)
        {
            return false;
        }

        if (MaxAge.HasValue && age > MaxAge.Value)
        {
            return false;
        }

        return true;
    }
}
