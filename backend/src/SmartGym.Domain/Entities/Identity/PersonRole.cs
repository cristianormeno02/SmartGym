namespace SmartGym.Domain.Entities.Identity;

public class PersonRole
{
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
