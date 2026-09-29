using SmartGym.Domain.Common;

namespace SmartGym.Domain.Entities.Operations;

public class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }

    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }

    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
