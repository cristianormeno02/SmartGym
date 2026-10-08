using SmartGym.Domain.Common;

namespace SmartGym.Domain.Entities.Activities;

public class Room : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Capacity { get; set; }
}
