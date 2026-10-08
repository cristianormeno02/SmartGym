using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Reservations.Services;

public class ReservationService : IReservationService
{
    private readonly ISmartGymDbContext _dbContext;

    public ReservationService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReservationDto> CreateReservationAsync(
        CreateReservationRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // 1. Bloquear la clase antes de leerla: serializa las reservas concurrentes para que el control
        //    de cupo, duplicados y posición en lista de espera se evalúe sobre datos actualizados.
        await _dbContext.LockClassSessionForUpdateAsync(request.ClassSessionId, cancellationToken);

        var session = await _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .FirstOrDefaultAsync(cs => cs.Id == request.ClassSessionId && cs.IsActive, cancellationToken);

        if (session == null)
        {
            throw new ArgumentException("La clase especificada no existe o no está activa.");
        }

        if (session.Status != ClassSessionStatus.Scheduled)
        {
            throw new InvalidOperationException($"No se pueden realizar reservas para una clase en estado {session.Status}.");
        }

        if (session.Activity.Status != ActivityStatus.Active)
        {
            throw new InvalidOperationException($"La actividad '{session.Activity.Name}' no está disponible para reservas.");
        }

        // 2. Obtener alumno
        var student = await _dbContext.People
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == request.StudentId && p.IsActive, cancellationToken);

        if (student == null || !student.PersonRoles.Any(pr => pr.IsActive && pr.Role.Name == RoleType.Student.ToString()))
        {
            throw new ArgumentException("El alumno especificado no existe o no tiene el rol de Alumno activo.");
        }

        // 3. Verificar si el alumno ya tiene una reserva activa para esta misma clase
        var existingReservation = await _dbContext.Reservations
            .AnyAsync(r => r.ClassSessionId == session.Id &&
                           r.StudentId == student.Id &&
                           (r.Status == ReservationStatus.Reserved || r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.WaitList),
                      cancellationToken);

        if (existingReservation)
        {
            throw new InvalidOperationException("El alumno ya posee una reserva o se encuentra en lista de espera para esta clase.");
        }

        // 4. Validar restricciones de edad
        if (student.BirthDate.HasValue)
        {
            var age = DateTime.UtcNow.Year - student.BirthDate.Value.Year;
            if (student.BirthDate.Value > DateTime.UtcNow.AddYears(-age))
            {
                age--;
            }

            if (!session.Activity.IsAgeAllowed(age))
            {
                throw new InvalidOperationException($"La actividad '{session.Activity.Name}' no admite alumnos con edad de {age} años.");
            }
        }

        // 5. Verificar membresía o pase individual disponible
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeMembership = await _dbContext.Memberships
            .Include(m => m.MembershipPlan)
                .ThenInclude(p => p.AllowedActivities)
            .Where(m => m.StudentId == student.Id && m.IsActive && m.Status == MembershipStatus.Active)
            .Where(m => m.StartDate <= session.Date && session.Date <= m.EndDate)
            .OrderByDescending(m => m.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        MembershipCreditMovement? creditMovement = null;
        Guid? membershipId = null;

        // 6. Control de cupos y resolución de estado
        Reservation reservation;

        if (session.ReservedCount < session.MaxCapacity)
        {
            // Hay cupo disponible en sala
            if (activeMembership != null)
            {
                if (!activeMembership.MembershipPlan.AllowsActivity(session.ActivityId))
                {
                    throw new InvalidOperationException($"El plan '{activeMembership.MembershipPlan.Name}' no incluye la actividad '{session.Activity.Name}'.");
                }

                creditMovement = activeMembership.DeductCredit(session.Id, $"Reserva de clase: {session.Activity.Name}", session.Date);
                _dbContext.MembershipCreditMovements.Add(creditMovement);
                membershipId = activeMembership.Id;
            }
            else
            {
                // Buscar si compró un ticket individual para esta clase
                var ticket = await _dbContext.MembershipCreditMovements
                    .FirstOrDefaultAsync(cm => cm.StudentId == student.Id &&
                                               cm.ClassSessionId == session.Id &&
                                               cm.Type == CreditMovementType.SingleClassTicket &&
                                               cm.IsActive,
                                         cancellationToken);

                if (ticket == null)
                {
                    throw new InvalidOperationException("El alumno no cuenta con una membresía activa que cubra esta actividad ni posee un ticket individual.");
                }

                creditMovement = ticket;
            }

            session.ReservedCount++;

            reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                ClassSessionId = session.Id,
                StudentId = student.Id,
                MembershipId = membershipId,
                CreditMovementId = creditMovement?.Id,
                Status = ReservationStatus.Reserved,
                ReservedAtUtc = DateTime.UtcNow,
                IsActive = true
            };
        }
        else
        {
            // Cupo agotado -> Se incorpora a Lista de Espera (WaitList)
            var currentWaitlistCount = await _dbContext.Reservations
                .CountAsync(r => r.ClassSessionId == session.Id && r.Status == ReservationStatus.WaitList, cancellationToken);

            reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                ClassSessionId = session.Id,
                StudentId = student.Id,
                MembershipId = activeMembership?.Id,
                Status = ReservationStatus.WaitList,
                WaitListPosition = currentWaitlistCount + 1,
                ReservedAtUtc = DateTime.UtcNow,
                IsActive = true
            };
        }

        _dbContext.Reservations.Add(reservation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        reservation.ClassSession = session;
        reservation.Student = student;

        return MapToDto(reservation);
    }

    public async Task<ReservationDto> CancelReservationAsync(
        Guid reservationId,
        CancelReservationRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var classSessionId = await _dbContext.Reservations
            .Where(r => r.Id == reservationId && r.IsActive)
            .Select(r => (Guid?)r.ClassSessionId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Reserva no encontrada.");

        // Bloquear la clase antes de leer la reserva: evita cancelaciones duplicadas (doble devolución de crédito)
        // y carreras con reservas concurrentes al liberar el cupo o promover la lista de espera.
        await _dbContext.LockClassSessionForUpdateAsync(classSessionId, cancellationToken);

        var reservation = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .Include(r => r.Membership)
                .ThenInclude(m => m!.MembershipPlan)
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.IsActive, cancellationToken);

        if (reservation == null)
        {
            throw new ArgumentException("Reserva no encontrada.");
        }

        if (reservation.Status != ReservationStatus.Reserved &&
            reservation.Status != ReservationStatus.Confirmed &&
            reservation.Status != ReservationStatus.WaitList)
        {
            throw new InvalidOperationException($"No se puede cancelar una reserva en estado {reservation.Status}.");
        }

        var session = reservation.ClassSession;
        var wasInWaitList = reservation.Status == ReservationStatus.WaitList;

        // Evaluación de política de cancelación (ventana límite de 2 horas)
        var sessionStartDateTime = session.Date.ToDateTime(session.StartTime);
        var hoursRemaining = (sessionStartDateTime - DateTime.UtcNow).TotalHours;
        var isLateCancellation = hoursRemaining < 2.0;

        Guid? refundMovementId = null;

        if (!wasInWaitList)
        {
            // Si la cancelación es con anticipación (>= 2 hs) y pertenecía a membresía, se devuelve el crédito
            if (!isLateCancellation && reservation.Membership != null)
            {
                var refundMovement = reservation.Membership.RefundCredit(session.Id, $"Devolución por cancelación anticipada ({hoursRemaining:F1}h antes)");
                _dbContext.MembershipCreditMovements.Add(refundMovement);
                refundMovementId = refundMovement.Id;
            }

            if (session.ReservedCount > 0)
            {
                session.ReservedCount--;
            }

            // Promoción automática del primero en lista de espera si existiere
            var firstInWaitList = await _dbContext.Reservations
                .Include(r => r.Membership)
                .Where(r => r.ClassSessionId == session.Id && r.Status == ReservationStatus.WaitList)
                .OrderBy(r => r.WaitListPosition)
                .FirstOrDefaultAsync(cancellationToken);

            if (firstInWaitList != null)
            {
                if (firstInWaitList.Membership != null && firstInWaitList.Membership.HasAvailableCredit())
                {
                    var debitMovement = firstInWaitList.Membership.DeductCredit(session.Id, "Promoción de lista de espera a reserva confirmada", session.Date);
                    _dbContext.MembershipCreditMovements.Add(debitMovement);
                    firstInWaitList.CreditMovementId = debitMovement.Id;
                }

                firstInWaitList.Status = ReservationStatus.Reserved;
                firstInWaitList.WaitListPosition = null;
                session.ReservedCount++;
            }
        }

        reservation.Cancel(request.Reason, isLateCancellation, refundMovementId);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapToDto(reservation);
    }

    public async Task<ReservationDto> RecordAttendanceAsync(
        RecordAttendanceRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var reservation = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .FirstOrDefaultAsync(r => r.Id == request.ReservationId && r.IsActive, cancellationToken);

        if (reservation == null)
        {
            throw new ArgumentException("Reserva no encontrada.");
        }

        if (reservation.Status != ReservationStatus.Reserved && reservation.Status != ReservationStatus.Confirmed)
        {
            throw new InvalidOperationException($"No se puede registrar asistencia para una reserva en estado {reservation.Status}.");
        }

        if (!reservation.Student.IsActive)
        {
            throw new InvalidOperationException("No se puede registrar asistencia para un alumno que no se encuentra activo.");
        }

        reservation.MarkAttended(request.Source, currentUserId);
        reservation.ClassSession.AttendedCount++;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(reservation);
    }

    public async Task<ReservationDto> RecordNoShowAsync(
        Guid reservationId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var reservation = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.IsActive, cancellationToken);

        if (reservation == null)
        {
            throw new ArgumentException("Reserva no encontrada.");
        }

        reservation.MarkNoShow();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(reservation);
    }

    public async Task<List<ReservationDto>> GetByStudentAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .AsNoTracking()
            .Where(r => r.StudentId == studentId && r.IsActive)
            .OrderByDescending(r => r.ReservedAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<List<ReservationDto>> GetByClassSessionAsync(Guid classSessionId, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .AsNoTracking()
            .Where(r => r.ClassSessionId == classSessionId && r.IsActive)
            .OrderBy(r => r.Status)
            .ThenBy(r => r.WaitListPosition)
            .ThenBy(r => r.ReservedAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<SessionAttendanceSummaryDto> GetSessionAttendanceSummaryAsync(Guid classSessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .AsNoTracking()
            .FirstOrDefaultAsync(cs => cs.Id == classSessionId && cs.IsActive, cancellationToken);

        if (session == null)
        {
            throw new ArgumentException("Clase no encontrada.");
        }

        var attendees = await GetByClassSessionAsync(classSessionId, cancellationToken);

        return new SessionAttendanceSummaryDto(
            session.Id,
            session.Activity?.Name ?? string.Empty,
            session.Date,
            session.MaxCapacity,
            session.ReservedCount,
            session.AttendedCount,
            attendees
        );
    }

    private static ReservationDto MapToDto(Reservation r) =>
        new(
            r.Id,
            r.ClassSessionId,
            r.ClassSession?.Activity?.Name ?? string.Empty,
            r.ClassSession?.Date ?? DateOnly.MinValue,
            r.ClassSession?.StartTime ?? TimeOnly.MinValue,
            r.ClassSession?.EndTime ?? TimeOnly.MinValue,
            r.ClassSession?.Room?.Name ?? string.Empty,
            r.StudentId,
            r.Student?.FullName ?? string.Empty,
            r.Student?.Dni ?? string.Empty,
            r.MembershipId,
            r.Status,
            r.ReservedAtUtc,
            r.ConfirmedAtUtc,
            r.CancelledAtUtc,
            r.CancellationReason,
            r.IsLateCancellation,
            r.AttendedAtUtc,
            r.AttendanceSource,
            r.WaitListPosition,
            r.Student?.Document?.Type,
            r.Student?.Document?.Number
        );
}
