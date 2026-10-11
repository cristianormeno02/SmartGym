using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Reservations.Services;

public class ReservationService : IReservationService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly SmartGym.Application.Common.IGymCalendar _gymCalendar;
    private readonly ICreditLedgerService _creditLedgerService;
    private readonly IMembershipEligibilityService _eligibilityService;

    public ReservationService(
        ISmartGymDbContext dbContext,
        SmartGym.Application.Common.IGymCalendar gymCalendar,
        ICreditLedgerService? creditLedgerService = null,
        IMembershipEligibilityService? eligibilityService = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
        _creditLedgerService = creditLedgerService ?? new CreditLedgerService(dbContext, gymCalendar);
        _eligibilityService = eligibilityService ?? new MembershipEligibilityService(dbContext, gymCalendar);
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

        if (session.Status != ClassSessionStatus.Open)
        {
            throw new InvalidOperationException($"No se pueden realizar reservas para una clase en estado {session.Status}.");
        }

        var nowUtc = _gymCalendar.UtcNow;
        if (nowUtc >= session.StartsAtUtc)
        {
            throw new InvalidOperationException("No se pueden realizar reservas para una clase que ya ha iniciado o pasado.");
        }

        if (session.Activity.Status != ActivityStatus.Active)
        {
            throw new InvalidOperationException($"[{SmartGym.Application.Modules.Memberships.Dtos.EligibilityIssueCodes.ActivityNotActive}] La actividad '{session.Activity.Name}' no está disponible para reservas.");
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

        // 4. Bloquear las membresías del alumno antes de leerlas, para elegir y consumir sobre saldos actualizados,
        //    y evaluar elegibilidad y selección mediante IMembershipEligibilityService
        await _dbContext.LockStudentMembershipsForUpdateAsync(student.Id, cancellationToken);

        var sessionLocalDate = DateOnly.FromDateTime(_gymCalendar.ToLocal(session.StartsAtUtc).DateTime);
        var selection = await _eligibilityService.SelectMembershipForAsync(
            studentId: student.Id,
            activityId: session.ActivityId,
            targetDate: sessionLocalDate,
            explicitMembershipId: request.MembershipId,
            classSessionId: session.Id,
            cancellationToken: cancellationToken);

        var activeMembership = selection.IsEligible ? selection.SelectedMembership : null;

        MembershipCreditMovement? creditMovement = null;
        Guid? membershipId = null;

        // 5. Control de cupos y resolución de estado
        Reservation reservation;

        if (session.ReservedCount < session.Capacity)
        {
            // Hay cupo disponible en sala
            if (activeMembership != null)
            {
                creditMovement = await _creditLedgerService.ConsumeCreditForReservationAsync(
                    activeMembership,
                    session.Id,
                    sessionLocalDate,
                    $"Reserva de clase: {session.Activity.Name}",
                    currentUserId,
                    cancellationToken);
                membershipId = activeMembership.Id;
            }
            else if (!request.MembershipId.HasValue)
            {
                // Si no tiene bloqueos de alumno/edad/actividad, verificar si compró un ticket individual para esta clase
                var blockingStudentOrActivityIssue = selection.Issues.FirstOrDefault(i =>
                    i.Code == SmartGym.Application.Modules.Memberships.Dtos.EligibilityIssueCodes.StudentNotActive ||
                    i.Code == SmartGym.Application.Modules.Memberships.Dtos.EligibilityIssueCodes.ActivityNotActive ||
                    i.Code == SmartGym.Application.Modules.Memberships.Dtos.EligibilityIssueCodes.AgeNotAllowed ||
                    i.Code == SmartGym.Application.Modules.Memberships.Dtos.EligibilityIssueCodes.MedicalCertificateRequired ||
                    i.Code == SmartGym.Application.Modules.Memberships.Dtos.EligibilityIssueCodes.UnderageEmergencyContactRequired);

                if (blockingStudentOrActivityIssue != null)
                {
                    throw new InvalidOperationException($"[{blockingStudentOrActivityIssue.Code}] {blockingStudentOrActivityIssue.Message}");
                }

                var ticket = await _dbContext.MembershipCreditMovements
                    .FirstOrDefaultAsync(cm => cm.StudentId == student.Id &&
                                               cm.ClassSessionId == session.Id &&
                                               cm.Type == CreditMovementType.SingleClassTicket &&
                                               cm.IsActive,
                                         cancellationToken);

                if (ticket == null)
                {
                    var reasons = selection.Issues.Count > 0
                        ? string.Join(" | ", selection.Issues.Select(i => $"[{i.Code}] {i.Message}"))
                        : "El alumno no cuenta con una membresía activa que cubra esta actividad ni posee un ticket individual.";
                    throw new InvalidOperationException(reasons);
                }

                creditMovement = ticket;
            }
            else
            {
                var reasons = selection.Issues.Count > 0
                    ? string.Join(" | ", selection.Issues.Select(i => $"[{i.Code}] {i.Message}"))
                    : "La membresía seleccionada no es elegible para esta clase.";
                throw new InvalidOperationException(reasons);
            }

            session.IncrementReservedCount();

            reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                ClassSessionId = session.Id,
                StudentId = student.Id,
                MembershipId = membershipId,
                CreditMovementId = creditMovement?.Id,
                Status = ReservationStatus.Reserved,
                ReservedAtUtc = nowUtc,
                IsActive = true
            };
        }
        else
        {
            if (activeMembership == null && request.MembershipId.HasValue)
            {
                var reasons = selection.Issues.Count > 0
                    ? string.Join(" | ", selection.Issues.Select(i => $"[{i.Code}] {i.Message}"))
                    : "La membresía seleccionada no es elegible para esta clase.";
                throw new InvalidOperationException(reasons);
            }

            // Cupo agotado -> Se incorpora a Lista de Espera (WaitList) sin consumir crédito aún
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
                ReservedAtUtc = nowUtc,
                IsActive = true
            };
        }

        _dbContext.Reservations.Add(reservation);
        await SaveChangesTranslatingConcurrencyAsync(cancellationToken);
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

        var reservationKeys = await _dbContext.Reservations
            .Where(r => r.Id == reservationId && r.IsActive)
            .Select(r => new { r.ClassSessionId, r.StudentId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("Reserva no encontrada.");
        var classSessionId = reservationKeys.ClassSessionId;

        // Bloquear la clase antes de leer la reserva: evita cancelaciones duplicadas (doble devolución de crédito)
        // y carreras con reservas concurrentes al liberar el cupo o promover la lista de espera.
        await _dbContext.LockClassSessionForUpdateAsync(classSessionId, cancellationToken);
        await _dbContext.LockStudentMembershipsForUpdateAsync(reservationKeys.StudentId, cancellationToken);

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
        var hoursRemaining = (session.StartsAtUtc - _gymCalendar.UtcNow).TotalHours;
        var isLateCancellation = hoursRemaining < 2.0;

        Guid? refundMovementId = null;

        if (!wasInWaitList)
        {
            // Si la cancelación es con anticipación (>= 2 hs) y pertenecía a membresía, se devuelve el crédito
            if (!isLateCancellation && reservation.Membership != null)
            {
                var refundMovement = await _creditLedgerService.RefundCreditForReservationAsync(
                    reservation.Membership,
                    session.Id,
                    $"Devolución por cancelación anticipada ({hoursRemaining:F1}h antes)",
                    currentUserId,
                    cancellationToken);
                refundMovementId = refundMovement.Id;
            }

            session.DecrementReservedCount();

            await PromoteFirstEligibleFromWaitListAsync(session, currentUserId, cancellationToken);
        }

        reservation.Cancel(request.Reason, isLateCancellation, refundMovementId);

        await SaveChangesTranslatingConcurrencyAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapToDto(reservation);
    }

    /// <summary>
    /// Ocupa el cupo liberado con el primero de la lista de espera que pueda usarlo. Quien se anotó con una
    /// membresía que ya no es utilizable para la clase (suspendida, vencida, sin créditos) conserva su lugar
    /// en la lista y se continúa con el siguiente, sin impedir la cancelación en curso.
    /// </summary>
    private async Task PromoteFirstEligibleFromWaitListAsync(
        ClassSession session,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var waitList = await _dbContext.Reservations
            .Where(r => r.ClassSessionId == session.Id && r.IsActive && r.Status == ReservationStatus.WaitList)
            .OrderBy(r => r.WaitListPosition)
            .ToListAsync(cancellationToken);

        var sessionLocalDate = DateOnly.FromDateTime(_gymCalendar.ToLocal(session.StartsAtUtc).DateTime);

        foreach (var candidate in waitList)
        {
            if (candidate.MembershipId.HasValue)
            {
                await _dbContext.LockStudentMembershipsForUpdateAsync(candidate.StudentId, cancellationToken);

                var selection = await _eligibilityService.SelectMembershipForAsync(
                    studentId: candidate.StudentId,
                    activityId: session.ActivityId,
                    targetDate: sessionLocalDate,
                    explicitMembershipId: candidate.MembershipId,
                    classSessionId: session.Id,
                    cancellationToken: cancellationToken);

                if (!selection.IsEligible || selection.SelectedMembership == null)
                {
                    continue;
                }

                var debitMovement = await _creditLedgerService.ConsumeCreditForReservationAsync(
                    selection.SelectedMembership,
                    session.Id,
                    sessionLocalDate,
                    "Promoción de lista de espera a reserva confirmada",
                    currentUserId,
                    cancellationToken);
                candidate.CreditMovementId = debitMovement.Id;
            }

            candidate.Status = ReservationStatus.Reserved;
            candidate.WaitListPosition = null;
            candidate.UpdatedAtUtc = _gymCalendar.UtcNow;
            session.IncrementReservedCount();
            return;
        }
    }

    private async Task SaveChangesTranslatingConcurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(
                "La membresía o la clase fue modificada por otra operación. Intente nuevamente.",
                ex);
        }
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
        reservation.ClassSession.IncrementAttendedCount();

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
        var localStart = _gymCalendar.ToLocal(session.StartsAtUtc);

        return new SessionAttendanceSummaryDto(
            session.Id,
            session.Activity?.Name ?? string.Empty,
            DateOnly.FromDateTime(localStart.DateTime),
            session.Capacity,
            session.ReservedCount,
            session.AttendedCount,
            attendees
        );
    }

    private ReservationDto MapToDto(Reservation r)
    {
        DateOnly classDate = DateOnly.MinValue;
        TimeOnly startTime = TimeOnly.MinValue;
        TimeOnly endTime = TimeOnly.MinValue;

        if (r.ClassSession != null)
        {
            var localStart = _gymCalendar.ToLocal(r.ClassSession.StartsAtUtc);
            var localEnd = _gymCalendar.ToLocal(r.ClassSession.EndsAtUtc);
            classDate = DateOnly.FromDateTime(localStart.DateTime);
            startTime = TimeOnly.FromDateTime(localStart.DateTime);
            endTime = TimeOnly.FromDateTime(localEnd.DateTime);
        }

        return new(
            r.Id,
            r.ClassSessionId,
            r.ClassSession?.Activity?.Name ?? string.Empty,
            classDate,
            startTime,
            endTime,
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
}
