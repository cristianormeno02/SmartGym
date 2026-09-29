using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public class NotificationService : INotificationService
{
    private readonly ISmartGymDbContext _dbContext;

    public NotificationService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<NotificationDto>> GetNotificationsForPersonAsync(Guid personId, bool onlyUnread = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.NotificationMessages
            .Include(n => n.RecipientPerson)
            .AsNoTracking()
            .Where(n => n.RecipientPersonId == personId);

        if (onlyUnread)
        {
            query = query.Where(n => !n.IsRead);
        }

        var list = await query
            .OrderByDescending(n => n.SentAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<NotificationDto> SendNotificationAsync(SendNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var recipient = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == request.RecipientPersonId && p.IsActive, cancellationToken);
        if (recipient == null)
        {
            throw new ArgumentException("El destinatario de la notificación no existe.");
        }

        var message = new NotificationMessage
        {
            Id = Guid.NewGuid(),
            RecipientPersonId = request.RecipientPersonId,
            Type = request.Type,
            Channel = request.Channel,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            IsRead = false,
            SentAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.NotificationMessages.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);

        message.RecipientPerson = recipient;
        return MapToDto(message);
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.NotificationMessages.FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);
        if (message == null) return false;

        message.IsRead = true;
        message.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task NotifyClassSuspendedAsync(Guid classSessionId, string reason, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .FirstOrDefaultAsync(cs => cs.Id == classSessionId, cancellationToken);

        if (session == null) return;

        var reservations = await _dbContext.Reservations
            .Include(r => r.Student)
            .Where(r => r.ClassSessionId == classSessionId &&
                       (r.Status == ReservationStatus.Reserved || r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.WaitList))
            .ToListAsync(cancellationToken);

        var notifications = reservations.Select(r => new NotificationMessage
        {
            Id = Guid.NewGuid(),
            RecipientPersonId = r.StudentId,
            Type = NotificationType.ClassSuspended,
            Channel = NotificationChannel.InApp,
            Title = $"Clase Suspendida: {session.Activity?.Name ?? "Actividad"} ({session.Date:dd/MM})",
            Body = $"Te informamos que la clase de {session.Activity?.Name ?? "actividad"} del día {session.Date:dd/MM} a las {session.StartTime:HH:mm} ha sido suspendida. Motivo: {reason}. Se ha reintegrado/compensado el crédito correspondiente.",
            IsRead = false,
            SentAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        }).ToList();

        if (notifications.Any())
        {
            _dbContext.NotificationMessages.AddRange(notifications);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static NotificationDto MapToDto(NotificationMessage n) =>
        new(
            n.Id,
            n.RecipientPersonId,
            n.RecipientPerson?.FullName ?? string.Empty,
            n.Type,
            n.Channel,
            n.Title,
            n.Body,
            n.IsRead,
            n.SentAtUtc
        );
}
