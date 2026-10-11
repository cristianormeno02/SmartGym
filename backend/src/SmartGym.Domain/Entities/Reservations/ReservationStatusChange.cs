using System.ComponentModel.DataAnnotations.Schema;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Reservations;

public class ReservationStatusChange
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReservationId { get; set; }
    public Reservation Reservation { get; set; } = null!;

    public ReservationStatus? PreviousStatus { get; set; }
    public ReservationStatus NewStatus { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Guid? ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }

    public ReservationChangeTrigger Trigger { get; set; }

    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
