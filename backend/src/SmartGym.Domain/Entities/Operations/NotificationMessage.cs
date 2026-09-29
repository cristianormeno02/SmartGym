using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Operations;

public class NotificationMessage : BaseEntity
{
    public Guid RecipientPersonId { get; set; }
    public Person RecipientPerson { get; set; } = null!;

    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public bool IsRead { get; set; } = false;
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
