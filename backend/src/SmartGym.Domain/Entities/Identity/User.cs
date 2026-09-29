using SmartGym.Domain.Common;

namespace SmartGym.Domain.Entities.Identity;

public class User : BaseEntity
{
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public string Username { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? GoogleSubjectId { get; set; }
    public bool IsEmailConfirmed { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public bool IsLockedOut { get; set; }
}
