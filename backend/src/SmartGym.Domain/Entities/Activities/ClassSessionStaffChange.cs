using System;
using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;

namespace SmartGym.Domain.Entities.Activities;

public class ClassSessionStaffChange : BaseEntity
{
    public Guid ClassSessionId { get; private set; }
    public ClassSession ClassSession { get; internal set; } = null!;

    public string Role { get; private set; } = null!; // "Instructor" | "Secretary"
    public Guid? PreviousPersonId { get; private set; }
    public Person? PreviousPerson { get; private set; }

    public Guid? NewPersonId { get; private set; }
    public Person? NewPerson { get; private set; }

    public string Reason { get; private set; } = null!;
    public Guid? ChangedByUserId { get; private set; }
    public User? ChangedByUser { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }

    private ClassSessionStaffChange() { }

    public static ClassSessionStaffChange Create(
        Guid classSessionId,
        string role,
        Guid? previousPersonId,
        Guid? newPersonId,
        string reason,
        Guid? changedByUserId,
        DateTime changedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(role) || (role != "Instructor" && role != "Secretary"))
        {
            throw new ArgumentException("El rol de personal debe ser Instructor o Secretary.", nameof(role));
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("El motivo del cambio de personal es obligatorio y no puede superar 500 caracteres.", nameof(reason));
        }

        return new ClassSessionStaffChange
        {
            Id = Guid.NewGuid(),
            ClassSessionId = classSessionId,
            Role = role,
            PreviousPersonId = previousPersonId,
            NewPersonId = newPersonId,
            Reason = reason.Trim(),
            ChangedByUserId = changedByUserId,
            ChangedAtUtc = changedAtUtc,
            CreatedAtUtc = changedAtUtc,
            UpdatedAtUtc = changedAtUtc,
            IsActive = true
        };
    }
}
