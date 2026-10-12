using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Operations;

public class NotificationPreference : BaseEntity
{
    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public NotificationCategory Category { get; set; }
    public NotificationChannel Channel { get; set; }
    public bool Enabled { get; set; }

    public static bool IsMandatoryChannel(NotificationCategory category, NotificationChannel channel)
    {
        if (category.IsTransactional() && channel == NotificationChannel.InApp)
        {
            return true;
        }

        if (category == NotificationCategory.Security && channel == NotificationChannel.Email)
        {
            return true;
        }

        return false;
    }

    public static bool GetDefaultEnabled(NotificationCategory category, NotificationChannel channel)
    {
        if (channel == NotificationChannel.InApp)
        {
            return true;
        }

        if (channel == NotificationChannel.Email)
        {
            return category.IsTransactional();
        }

        return false;
    }

    public static NotificationPreference CreateOrUpdate(
        Guid personId,
        NotificationCategory category,
        NotificationChannel channel,
        bool enabled,
        DateTime nowUtc)
    {
        if (!enabled && IsMandatoryChannel(category, channel))
        {
            throw new InvalidOperationException(
                $"El canal {channel} para la categoría transaccional {category} es obligatorio y no puede deshabilitarse.");
        }

        return new NotificationPreference
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            Category = category,
            Channel = channel,
            Enabled = enabled,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            IsActive = true
        };
    }

    public void SetEnabled(bool enabled, DateTime nowUtc)
    {
        if (!enabled && IsMandatoryChannel(Category, Channel))
        {
            throw new InvalidOperationException(
                $"El canal {Channel} para la categoría transaccional {Category} es obligatorio y no puede deshabilitarse.");
        }

        Enabled = enabled;
        UpdatedAtUtc = nowUtc;
    }
}
