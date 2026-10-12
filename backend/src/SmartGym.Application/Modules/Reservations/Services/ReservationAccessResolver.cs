using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Reservations.Services;

public record ResolvedReservationAccess(
    ReservationAccessType AccessType,
    Membership? SelectedMembership = null,
    MembershipCreditMovement? SelectedMovement = null,
    Guid? ConfirmedPaymentId = null,
    string? PromotionCode = null
);

public record ConsumedReservationAccess(
    ReservationAccessType AccessType,
    Guid? MembershipId = null,
    Guid? CreditMovementId = null,
    Guid? SingleClassTicketMovementId = null,
    Guid? PaymentId = null,
    Guid? PromotionRedemptionId = null
);

public record AccessResolutionRequest(
    Guid? MembershipId = null,
    Guid? SingleClassTicketMovementId = null,
    string? PromotionCode = null
);

public interface IReservationAccessResolver
{
    Task<ResolvedReservationAccess> ResolveAsync(
        Person student,
        ClassSession session,
        DateOnly sessionLocalDate,
        AccessResolutionRequest request,
        Guid? excludingReservationId = null,
        CancellationToken cancellationToken = default);

    Task<ConsumedReservationAccess> ConsumeAsync(
        ResolvedReservationAccess resolvedAccess,
        Person student,
        ClassSession session,
        DateOnly sessionLocalDate,
        Guid reservationId,
        Guid? currentUserId,
        CancellationToken cancellationToken = default);
}

public class ReservationAccessResolver : IReservationAccessResolver
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IMembershipEligibilityService _eligibilityService;
    private readonly ICreditLedgerService _creditLedgerService;
    private readonly IPromotionService _promotionService;
    private readonly GymOptions _gymOptions;

    public ReservationAccessResolver(
        ISmartGymDbContext dbContext,
        IMembershipEligibilityService eligibilityService,
        ICreditLedgerService creditLedgerService,
        IPromotionService promotionService,
        IOptions<GymOptions>? gymOptions = null)
    {
        _dbContext = dbContext;
        _eligibilityService = eligibilityService;
        _creditLedgerService = creditLedgerService;
        _promotionService = promotionService;
        _gymOptions = gymOptions?.Value ?? new GymOptions();
    }

    public async Task<ResolvedReservationAccess> ResolveAsync(
        Person student,
        ClassSession session,
        DateOnly sessionLocalDate,
        AccessResolutionRequest request,
        Guid? excludingReservationId = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Evaluar primero los bloqueos de actividad y de persona (aplican a todas las modalidades)
        await ValidateActivityAndStudentRulesAsync(student, session, sessionLocalDate, cancellationToken);

        // 2. Orden determinista de resolución:
        //    (a) PromotionCode explícito
        if (!string.IsNullOrWhiteSpace(request.PromotionCode))
        {
            var code = request.PromotionCode.Trim();
            await _promotionService.EvaluatePromotionForReservationAsync(
                student.Id,
                session.Id,
                session.ActivityId,
                code,
                cancellationToken);

            return new ResolvedReservationAccess(
                ReservationAccessType.PromotionalBenefit,
                PromotionCode: code);
        }

        //    (b) SingleClassTicketMovementId explícito (pase individual o crédito compensatorio autónomo)
        if (request.SingleClassTicketMovementId.HasValue)
        {
            return await ResolveExplicitMovementAsync(
                student.Id,
                session,
                sessionLocalDate,
                request.SingleClassTicketMovementId.Value,
                excludingReservationId,
                cancellationToken);
        }

        //    (c) MembershipId explícito
        if (request.MembershipId.HasValue)
        {
            var explicitSelection = await _eligibilityService.SelectMembershipForAsync(
                studentId: student.Id,
                activityId: session.ActivityId,
                targetDate: sessionLocalDate,
                explicitMembershipId: request.MembershipId.Value,
                classSessionId: session.Id,
                cancellationToken: cancellationToken);

            if (!explicitSelection.IsEligible || explicitSelection.SelectedMembership == null)
            {
                var primaryCode = explicitSelection.Issues.FirstOrDefault()?.Code ?? ReservationErrorCodes.NoAccessRight;
                var reasonsText = explicitSelection.Issues.Count > 0
                    ? string.Join(" | ", explicitSelection.Issues.Select(i => $"[{i.Code}] {i.Message}"))
                    : "La membresía seleccionada no es elegible para esta clase.";

                throw ConflictException.WithCode(primaryCode, reasonsText, explicitSelection.Issues);
            }

            var accessType = IsUnlimited(explicitSelection.SelectedMembership)
                ? ReservationAccessType.UnlimitedMembership
                : ReservationAccessType.MembershipCredit;

            return new ResolvedReservationAccess(
                accessType,
                SelectedMembership: explicitSelection.SelectedMembership);
        }

        //    (d) Selección automática:
        //        1) Membresía elegible
        var autoSelection = await _eligibilityService.SelectMembershipForAsync(
            studentId: student.Id,
            activityId: session.ActivityId,
            targetDate: sessionLocalDate,
            explicitMembershipId: null,
            classSessionId: session.Id,
            cancellationToken: cancellationToken);

        if (autoSelection.IsEligible && autoSelection.SelectedMembership != null)
        {
            var accessType = IsUnlimited(autoSelection.SelectedMembership)
                ? ReservationAccessType.UnlimitedMembership
                : ReservationAccessType.MembershipCredit;

            return new ResolvedReservationAccess(
                accessType,
                SelectedMembership: autoSelection.SelectedMembership);
        }

        var heldMovementIds = await GetHeldMovementIdsAsync(student.Id, excludingReservationId, cancellationToken);

        //        2) Pase individual del alumno para esa clase
        var tickets = await _dbContext.MembershipCreditMovements
            .Where(cm => cm.StudentId == student.Id &&
                         cm.ClassSessionId == session.Id &&
                         cm.Type == CreditMovementType.SingleClassTicket &&
                         cm.IsActive &&
                         !heldMovementIds.Contains(cm.Id))
            .OrderBy(cm => cm.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (tickets.Count > 0)
        {
            var completedPaymentId = await ValidateSingleClassTicketPaymentAsync(student.Id, session.Id, cancellationToken);
            return new ResolvedReservationAccess(
                ReservationAccessType.SingleClassTicket,
                SelectedMovement: tickets[0],
                ConfirmedPaymentId: completedPaymentId);
        }

        //        3) Crédito compensatorio autónomo vigente y aplicable a la actividad (menor ExpiresOn primero)
        var compensatoryCredits = await _dbContext.MembershipCreditMovements
            .Where(cm => cm.StudentId == student.Id &&
                         cm.MembershipId == null &&
                         cm.Type == CreditMovementType.ClassSuspensionCompensation &&
                         cm.Amount > 0 &&
                         cm.IsActive &&
                         !heldMovementIds.Contains(cm.Id) &&
                         (!cm.ExpiresOn.HasValue || cm.ExpiresOn.Value >= sessionLocalDate) &&
                         (!cm.ActivityId.HasValue || cm.ActivityId.Value == session.ActivityId))
            .ToListAsync(cancellationToken);

        var selectedCompensatory = compensatoryCredits
            .OrderBy(cm => cm.ExpiresOn.HasValue ? 0 : 1)
            .ThenBy(cm => cm.ExpiresOn)
            .ThenBy(cm => cm.CreatedAtUtc)
            .FirstOrDefault();

        if (selectedCompensatory != null)
        {
            return new ResolvedReservationAccess(
                ReservationAccessType.CompensatoryCredit,
                SelectedMovement: selectedCompensatory);
        }

        //        4) Sin derecho de acceso válido -> 409 NO_ACCESS_RIGHT con motivos
        var issues = autoSelection.Issues.Count > 0
            ? autoSelection.Issues
            : [new EligibilityIssueDto(ReservationErrorCodes.NoAccessRight, "El alumno no dispone de un derecho de acceso válido para esta clase.")];

        var message = string.Join(" | ", issues.Select(i => $"[{i.Code}] {i.Message}"));
        throw ConflictException.WithCode(ReservationErrorCodes.NoAccessRight, message, issues);
    }

    public async Task<ConsumedReservationAccess> ConsumeAsync(
        ResolvedReservationAccess resolvedAccess,
        Person student,
        ClassSession session,
        DateOnly sessionLocalDate,
        Guid reservationId,
        Guid? currentUserId,
        CancellationToken cancellationToken = default)
    {
        switch (resolvedAccess.AccessType)
        {
            case ReservationAccessType.MembershipCredit:
            case ReservationAccessType.UnlimitedMembership:
            {
                var membership = resolvedAccess.SelectedMembership
                    ?? throw new InvalidOperationException("No se encontró la membresía resuelta para consumir.");

                var movement = await _creditLedgerService.ConsumeCreditForReservationAsync(
                    membership,
                    session.Id,
                    sessionLocalDate,
                    $"Reserva de clase: {session.Activity?.Name ?? string.Empty}",
                    currentUserId,
                    reservationId,
                    cancellationToken);

                return new ConsumedReservationAccess(
                    resolvedAccess.AccessType,
                    MembershipId: membership.Id,
                    CreditMovementId: movement.Id);
            }

            case ReservationAccessType.SingleClassTicket:
            {
                var movement = resolvedAccess.SelectedMovement
                    ?? throw new InvalidOperationException("No se encontró el pase individual resuelto.");

                return new ConsumedReservationAccess(
                    ReservationAccessType.SingleClassTicket,
                    SingleClassTicketMovementId: movement.Id,
                    PaymentId: resolvedAccess.ConfirmedPaymentId);
            }

            case ReservationAccessType.CompensatoryCredit:
            {
                var movement = resolvedAccess.SelectedMovement
                    ?? throw new InvalidOperationException("No se encontró el crédito compensatorio resuelto.");

                return new ConsumedReservationAccess(
                    ReservationAccessType.CompensatoryCredit,
                    SingleClassTicketMovementId: movement.Id);
            }

            case ReservationAccessType.PromotionalBenefit:
            {
                var code = resolvedAccess.PromotionCode
                    ?? throw new InvalidOperationException("No se encontró el código promocional resuelto.");

                var redemption = await _promotionService.RedeemPromotionForReservationAsync(
                    student.Id,
                    session.Id,
                    session.ActivityId,
                    code,
                    reservationId,
                    cancellationToken);

                return new ConsumedReservationAccess(
                    ReservationAccessType.PromotionalBenefit,
                    PromotionRedemptionId: redemption.Id);
            }

            default:
                throw new InvalidOperationException($"Modalidad de acceso no soportada: {resolvedAccess.AccessType}.");
        }
    }

    private Task ValidateActivityAndStudentRulesAsync(
        Person student,
        ClassSession session,
        DateOnly sessionLocalDate,
        CancellationToken cancellationToken)
    {
        if (session.Activity == null || !session.Activity.IsActive || session.Activity.Status != ActivityStatus.Active)
        {
            var activityName = session.Activity?.Name ?? session.ActivityId.ToString();
            var issue = new EligibilityIssueDto(
                EligibilityIssueCodes.ActivityNotActive,
                $"La actividad '{activityName}' no está disponible para reservas.");
            throw ConflictException.WithCode(
                EligibilityIssueCodes.ActivityNotActive,
                $"[{issue.Code}] {issue.Message}",
                new[] { issue });
        }

        var hasStudentRole = student.PersonRoles.Any(pr =>
            pr.IsActive &&
            (pr.RoleId == (int)RoleType.Student ||
             (pr.Role != null && pr.Role.Name == RoleType.Student.ToString())));

        if (!student.IsActive || student.Status != PersonStatus.Active || !hasStudentRole)
        {
            var issue = new EligibilityIssueDto(
                EligibilityIssueCodes.StudentNotActive,
                "El alumno especificado no existe o no tiene el rol de Alumno activo.");
            throw ConflictException.WithCode(
                EligibilityIssueCodes.StudentNotActive,
                $"[{issue.Code}] {issue.Message}",
                new[] { issue });
        }

        if (student.BirthDate.HasValue)
        {
            var birthDate = DateOnly.FromDateTime(student.BirthDate.Value);
            var age = CalculateAge(birthDate, sessionLocalDate);

            if (!session.Activity.IsAgeAllowed(age))
            {
                var issue = new EligibilityIssueDto(
                    EligibilityIssueCodes.AgeNotAllowed,
                    $"La actividad '{session.Activity.Name}' no admite alumnos de {age} años.");
                throw ConflictException.WithCode(
                    EligibilityIssueCodes.AgeNotAllowed,
                    $"[{issue.Code}] {issue.Message}",
                    new[] { issue });
            }

            if (age < 18 && !student.HasValidEmergencyContact())
            {
                var issue = new EligibilityIssueDto(
                    EligibilityIssueCodes.UnderageEmergencyContactRequired,
                    "El alumno menor de edad requiere un contacto de emergencia válido para acceder.");
                throw ConflictException.WithCode(
                    EligibilityIssueCodes.UnderageEmergencyContactRequired,
                    $"[{issue.Code}] {issue.Message}",
                    new[] { issue });
            }
        }

        if (_gymOptions.RequireValidMedicalCertificateForAccess)
        {
            var hasValidCert = student.MedicalCertificateExpiration.HasValue &&
                               DateOnly.FromDateTime(student.MedicalCertificateExpiration.Value) >= sessionLocalDate;

            if (!hasValidCert)
            {
                var issue = new EligibilityIssueDto(
                    EligibilityIssueCodes.MedicalCertificateRequired,
                    "El gimnasio exige un certificado de aptitud médica vigente.");
                throw ConflictException.WithCode(
                    EligibilityIssueCodes.MedicalCertificateRequired,
                    $"[{issue.Code}] {issue.Message}",
                    new[] { issue });
            }
        }

        return Task.CompletedTask;
    }

    private async Task<ResolvedReservationAccess> ResolveExplicitMovementAsync(
        Guid studentId,
        ClassSession session,
        DateOnly sessionLocalDate,
        Guid movementId,
        Guid? excludingReservationId,
        CancellationToken cancellationToken)
    {
        var movement = await _dbContext.MembershipCreditMovements
            .FirstOrDefaultAsync(cm => cm.Id == movementId && cm.StudentId == studentId && cm.IsActive, cancellationToken);

        if (movement == null)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.NoAccessRight,
                "El pase o crédito compensatorio indicado no existe o no pertenece al alumno.");
        }

        var heldMovementIds = await GetHeldMovementIdsAsync(studentId, excludingReservationId, cancellationToken);
        if (heldMovementIds.Contains(movement.Id))
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.AccessRightInUse,
                "El pase o crédito compensatorio indicado ya se encuentra utilizado por otra reserva.");
        }

        if (movement.Type == CreditMovementType.SingleClassTicket)
        {
            if (movement.ClassSessionId != session.Id)
            {
                throw ConflictException.WithCode(
                    ReservationErrorCodes.NoAccessRight,
                    "El pase individual indicado corresponde a otra clase.");
            }

            var completedPaymentId = await ValidateSingleClassTicketPaymentAsync(studentId, session.Id, cancellationToken);
            return new ResolvedReservationAccess(
                ReservationAccessType.SingleClassTicket,
                SelectedMovement: movement,
                ConfirmedPaymentId: completedPaymentId);
        }

        if (movement.Type == CreditMovementType.ClassSuspensionCompensation && movement.MembershipId == null)
        {
            if (movement.ExpiresOn.HasValue && movement.ExpiresOn.Value < sessionLocalDate)
            {
                throw ConflictException.WithCode(
                    ReservationErrorCodes.NoAccessRight,
                    "El crédito compensatorio indicado se encuentra vencido para la fecha de la clase.");
            }

            if (movement.ActivityId.HasValue && movement.ActivityId.Value != session.ActivityId)
            {
                var activityName = session.Activity?.Name ?? session.ActivityId.ToString();
                var issue = new EligibilityIssueDto(
                    EligibilityIssueCodes.ActivityNotAllowed,
                    $"El crédito compensatorio no habilita la actividad '{activityName}'.");
                throw ConflictException.WithCode(
                    EligibilityIssueCodes.ActivityNotAllowed,
                    $"[{issue.Code}] {issue.Message}",
                    new[] { issue });
            }

            return new ResolvedReservationAccess(
                ReservationAccessType.CompensatoryCredit,
                SelectedMovement: movement);
        }

        throw ConflictException.WithCode(
            ReservationErrorCodes.NoAccessRight,
            "El movimiento indicado no corresponde a un pase individual ni a un crédito compensatorio autónomo.");
    }

    private async Task<Guid?> ValidateSingleClassTicketPaymentAsync(
        Guid studentId,
        Guid classSessionId,
        CancellationToken cancellationToken)
    {
        var payments = await _dbContext.Payments
            .Where(p => p.IsActive && p.PayerPersonId == studentId && p.ClassSessionId == classSessionId)
            .OrderByDescending(p => p.PaidAtUtc)
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
        {
            return null;
        }

        // Un pago devuelto por completo ya no respalda el pase
        var completed = payments.FirstOrDefault(p => p.Status.HasNetAmount());
        if (completed == null)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.PaymentNotConfirmed,
                "El pago del pase individual aún no se encuentra confirmado.");
        }

        return completed.Id;
    }

    private async Task<HashSet<Guid>> GetHeldMovementIdsAsync(
        Guid studentId,
        Guid? excludingReservationId,
        CancellationToken cancellationToken)
    {
        var heldBySingleTicketColumn = await _dbContext.Reservations
            .Where(r => r.StudentId == studentId &&
                        r.IsActive &&
                        r.SingleClassTicketMovementId != null &&
                        (!excludingReservationId.HasValue || r.Id != excludingReservationId.Value) &&
                        (r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Attended ||
                         r.Status == ReservationStatus.NoShow ||
                         r.Status == ReservationStatus.Pending ||
                         (r.Status == ReservationStatus.Cancelled && r.IsLateCancellation)))
            .Select(r => r.SingleClassTicketMovementId!.Value)
            .ToListAsync(cancellationToken);

        var set = new HashSet<Guid>(heldBySingleTicketColumn);

        var heldByLegacyColumn = await _dbContext.Reservations
            .Where(r => r.StudentId == studentId &&
                        r.IsActive &&
                        r.MembershipId == null &&
                        r.CreditMovementId != null &&
                        (!excludingReservationId.HasValue || r.Id != excludingReservationId.Value) &&
                        (r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Attended ||
                         r.Status == ReservationStatus.NoShow ||
                         r.Status == ReservationStatus.Pending ||
                         (r.Status == ReservationStatus.Cancelled && r.IsLateCancellation)))
            .Select(r => r.CreditMovementId!.Value)
            .ToListAsync(cancellationToken);

        foreach (var id in heldByLegacyColumn)
        {
            set.Add(id);
        }

        return set;
    }

    private static bool IsUnlimited(Membership membership)
    {
        return membership.IsUnlimited || (membership.MembershipPlan?.IsUnlimited ?? false);
    }

    private static int CalculateAge(DateOnly birthDate, DateOnly referenceDate)
    {
        var age = referenceDate.Year - birthDate.Year;
        if (referenceDate < birthDate.AddYears(age))
        {
            age--;
        }
        return age;
    }
}
