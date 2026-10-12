using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Memberships.Services;

public class MembershipEligibilityService : IMembershipEligibilityService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly GymOptions _gymOptions;

    public MembershipEligibilityService(
        ISmartGymDbContext dbContext,
        IGymCalendar? gymCalendar = null,
        IOptions<GymOptions>? gymOptions = null)
    {
        _dbContext = dbContext;
        _gymOptions = gymOptions?.Value ?? new GymOptions();
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(_gymOptions));
    }

    public async Task<MembershipEligibilityDto> EvaluateEligibilityAsync(
        Guid membershipId,
        DateOnly? targetDate = null,
        Guid? activityId = null,
        Guid? classSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var membership = await _dbContext.Memberships
            .AsNoTracking()
            .Include(m => m.Student)
                .ThenInclude(p => p.PersonRoles)
                    .ThenInclude(pr => pr.Role)
            .Include(m => m.MembershipPlan)
                .ThenInclude(p => p.AllowedActivities)
            .Include(m => m.AllowedActivities)
            .Include(m => m.Suspensions)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.IsActive, cancellationToken)
            ?? throw new NotFoundException("La membresía especificada no existe.");

        var (resolvedDate, resolvedActivityId, activity) = await ResolveContextAsync(
            targetDate,
            activityId,
            classSessionId,
            cancellationToken);

        return EvaluateMembership(membership, membership.Student, resolvedDate, resolvedActivityId, classSessionId, activity);
    }

    public async Task<MembershipSelectionResultDto> SelectMembershipForAsync(
        Guid studentId,
        Guid? activityId = null,
        DateOnly? targetDate = null,
        Guid? explicitMembershipId = null,
        Guid? classSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var (resolvedDate, resolvedActivityId, activity) = await ResolveContextAsync(
            targetDate,
            activityId,
            classSessionId,
            cancellationToken);

        if (explicitMembershipId.HasValue)
        {
            var explicitMembership = await _dbContext.Memberships
                .Include(m => m.Student)
                    .ThenInclude(p => p.PersonRoles)
                        .ThenInclude(pr => pr.Role)
                .Include(m => m.MembershipPlan)
                    .ThenInclude(p => p.AllowedActivities)
                .Include(m => m.AllowedActivities)
                .Include(m => m.Suspensions)
                .FirstOrDefaultAsync(
                    m => m.Id == explicitMembershipId.Value && m.StudentId == studentId && m.IsActive,
                    cancellationToken)
                ?? throw new NotFoundException("La membresía especificada no existe o no pertenece al alumno.");

            var explicitEligibility = EvaluateMembership(
                explicitMembership,
                explicitMembership.Student,
                resolvedDate,
                resolvedActivityId,
                classSessionId,
                activity);

            return explicitEligibility.IsEligible
                ? new MembershipSelectionResultDto(true, explicitMembership, explicitEligibility, explicitEligibility.Issues)
                : new MembershipSelectionResultDto(false, null, explicitEligibility, explicitEligibility.Issues);
        }

        var memberships = await _dbContext.Memberships
            .Include(m => m.Student)
                .ThenInclude(p => p.PersonRoles)
                    .ThenInclude(pr => pr.Role)
            .Include(m => m.MembershipPlan)
                .ThenInclude(p => p.AllowedActivities)
            .Include(m => m.AllowedActivities)
            .Include(m => m.Suspensions)
            .Where(m => m.StudentId == studentId && m.IsActive)
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
        {
            var student = await _dbContext.People
                .AsNoTracking()
                .Include(p => p.PersonRoles)
                    .ThenInclude(pr => pr.Role)
                .FirstOrDefaultAsync(p => p.Id == studentId, cancellationToken);

            var issues = new List<EligibilityIssueDto>();
            if (!IsStudentActive(student))
            {
                issues.Add(new EligibilityIssueDto(
                    EligibilityIssueCodes.StudentNotActive,
                    "El alumno no se encuentra activo o no posee el rol de Alumno activo."));
            }
            else
            {
                issues.Add(new EligibilityIssueDto(
                    EligibilityIssueCodes.NoAvailableCredits,
                    "El alumno no posee ninguna membresía registrada."));
            }

            return new MembershipSelectionResultDto(false, null, null, issues);
        }

        var evaluated = memberships
            .Select(m => (
                Membership: m,
                Eligibility: EvaluateMembership(m, m.Student, resolvedDate, resolvedActivityId, classSessionId, activity)))
            .ToList();

        var eligibleOrdered = evaluated
            .Where(x => x.Eligibility.IsEligible)
            .OrderBy(x => x.Membership.IsUnlimited ? 0 : 1)
            .ThenBy(x => x.Membership.EndDate)
            .ThenBy(x => x.Membership.PurchasedAtUtc)
            .ThenBy(x => x.Membership.Id)
            .FirstOrDefault();

        if (eligibleOrdered.Membership != null)
        {
            return new MembershipSelectionResultDto(
                true,
                eligibleOrdered.Membership,
                eligibleOrdered.Eligibility,
                eligibleOrdered.Eligibility.Issues);
        }

        var bestIneligible = evaluated
            .OrderBy(x => GetCandidateStatusPriority(x.Eligibility.EffectiveStatus))
            .ThenBy(x => x.Membership.IsUnlimited ? 0 : 1)
            .ThenBy(x => x.Membership.EndDate)
            .ThenBy(x => x.Membership.PurchasedAtUtc)
            .ThenBy(x => x.Membership.Id)
            .First();

        return new MembershipSelectionResultDto(
            false,
            null,
            bestIneligible.Eligibility,
            bestIneligible.Eligibility.Issues);
    }

    public Task<IReadOnlyDictionary<Guid, SessionEligibilityEvaluationDto>> EvaluateForSessionsAsync(
        Guid studentId,
        IReadOnlyList<ClassSession> sessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var contexts = sessions
            .Select(cs => new SessionEligibilityContextDto(
                cs.Id,
                cs.ActivityId,
                DateOnly.FromDateTime(_gymCalendar.ToLocal(cs.StartsAtUtc).DateTime),
                cs.Activity))
            .ToList();

        return EvaluateForSessionsAsync(studentId, contexts, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, SessionEligibilityEvaluationDto>> EvaluateForSessionsAsync(
        Guid studentId,
        IReadOnlyList<SessionEligibilityContextDto> sessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (sessions.Count == 0)
        {
            return new Dictionary<Guid, SessionEligibilityEvaluationDto>();
        }

        // 1. Single load of student with roles
        var student = await _dbContext.People
            .AsNoTracking()
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == studentId, cancellationToken);

        // 2. Single load of any missing activities
        var missingActivityIds = sessions
            .Where(s => s.Activity == null)
            .Select(s => s.ActivityId)
            .Distinct()
            .ToList();

        var loadedActivities = missingActivityIds.Count > 0
            ? await _dbContext.Activities
                .AsNoTracking()
                .Where(a => missingActivityIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, cancellationToken)
            : new Dictionary<Guid, Activity>();

        // 3. Single load of student memberships with allowed activities & suspensions
        var memberships = await _dbContext.Memberships
            .AsNoTracking()
            .Include(m => m.MembershipPlan)
                .ThenInclude(p => p.AllowedActivities)
            .Include(m => m.AllowedActivities)
            .Include(m => m.Suspensions)
            .Where(m => m.StudentId == studentId && m.IsActive)
            .ToListAsync(cancellationToken);

        // 4. Single load of held movement IDs and autonomous tickets / compensatory credits
        var heldMovementIds = (await _dbContext.Reservations
            .AsNoTracking()
            .Where(r => r.StudentId == studentId &&
                        r.IsActive &&
                        r.SingleClassTicketMovementId != null &&
                        (r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Attended ||
                         r.Status == ReservationStatus.NoShow ||
                         r.Status == ReservationStatus.Pending ||
                         (r.Status == ReservationStatus.Cancelled && r.IsLateCancellation)))
            .Select(r => r.SingleClassTicketMovementId!.Value)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var accessMovements = await _dbContext.MembershipCreditMovements
            .AsNoTracking()
            .Where(cm => cm.StudentId == studentId &&
                         cm.IsActive &&
                         cm.MembershipId == null &&
                         (cm.Type == CreditMovementType.SingleClassTicket ||
                          cm.Type == CreditMovementType.ClassSuspensionCompensation))
            .ToListAsync(cancellationToken);

        var sessionIds = sessions.Select(s => s.ClassSessionId).Distinct().ToList();
        var sessionPayments = await _dbContext.Payments
            .AsNoTracking()
            .Where(p => p.PayerPersonId == studentId &&
                        p.IsActive &&
                        p.ClassSessionId != null &&
                        sessionIds.Contains(p.ClassSessionId.Value))
            .ToListAsync(cancellationToken);

        var results = new Dictionary<Guid, SessionEligibilityEvaluationDto>(sessions.Count);

        foreach (var session in sessions)
        {
            var activity = session.Activity ?? loadedActivities.GetValueOrDefault(session.ActivityId);
            results[session.ClassSessionId] = EvaluateSingleSessionInMemory(
                student,
                session,
                activity,
                memberships,
                accessMovements,
                heldMovementIds,
                sessionPayments);
        }

        return results;
    }

    private SessionEligibilityEvaluationDto EvaluateSingleSessionInMemory(
        Person? student,
        SessionEligibilityContextDto session,
        Activity? activity,
        IReadOnlyList<Membership> memberships,
        IReadOnlyList<MembershipCreditMovement> accessMovements,
        HashSet<Guid> heldMovementIds,
        IReadOnlyList<SmartGym.Domain.Entities.Operations.Payment> sessionPayments)
    {
        var evalDate = session.SessionLocalDate;
        var blockingIssues = new List<EligibilityIssueDto>();

        if (!IsStudentActive(student))
        {
            blockingIssues.Add(new EligibilityIssueDto(
                EligibilityIssueCodes.StudentNotActive,
                "El alumno no se encuentra en estado activo o no tiene el rol de Alumno activo."));
        }

        if (activity == null || !activity.IsActive || activity.Status != ActivityStatus.Active)
        {
            blockingIssues.Add(new EligibilityIssueDto(
                EligibilityIssueCodes.ActivityNotActive,
                "La actividad consultada no existe o no se encuentra activa."));
        }
        else if (student?.BirthDate != null)
        {
            var birthDateOnly = DateOnly.FromDateTime(student.BirthDate.Value);
            var age = CalculateAge(birthDateOnly, evalDate);
            if (!activity.IsAgeAllowed(age))
            {
                blockingIssues.Add(new EligibilityIssueDto(
                    EligibilityIssueCodes.AgeNotAllowed,
                    $"La actividad '{activity.Name}' no admite alumnos de {age} años."));
            }
        }

        if (student != null)
        {
            if (student.BirthDate.HasValue)
            {
                var birthDateOnly = DateOnly.FromDateTime(student.BirthDate.Value);
                var age = CalculateAge(birthDateOnly, evalDate);
                if (age < 18 && !student.HasValidEmergencyContact())
                {
                    blockingIssues.Add(new EligibilityIssueDto(
                        EligibilityIssueCodes.UnderageEmergencyContactRequired,
                        "El alumno menor de edad requiere un contacto de emergencia válido para acceder."));
                }
            }

            if (_gymOptions.RequireValidMedicalCertificateForAccess)
            {
                var hasValidCert = student.MedicalCertificateExpiration.HasValue &&
                                   DateOnly.FromDateTime(student.MedicalCertificateExpiration.Value) >= evalDate;
                if (!hasValidCert)
                {
                    blockingIssues.Add(new EligibilityIssueDto(
                        EligibilityIssueCodes.MedicalCertificateRequired,
                        "El gimnasio exige un certificado de aptitud médica vigente."));
                }
            }
        }

        if (blockingIssues.Count > 0)
        {
            return new SessionEligibilityEvaluationDto(
                session.ClassSessionId,
                false,
                null,
                null,
                null,
                0,
                blockingIssues);
        }

        // 1. Evaluate memberships in deterministic order
        var evaluatedMemberships = memberships
            .Select(m => (
                Membership: m,
                Eligibility: EvaluateMembership(m, student, evalDate, session.ActivityId, session.ClassSessionId, activity)))
            .ToList();

        var eligibleMembership = evaluatedMemberships
            .Where(x => x.Eligibility.IsEligible)
            .OrderBy(x => x.Membership.IsUnlimited ? 0 : 1)
            .ThenBy(x => x.Membership.EndDate)
            .ThenBy(x => x.Membership.PurchasedAtUtc)
            .ThenBy(x => x.Membership.Id)
            .FirstOrDefault();

        if (eligibleMembership.Membership != null)
        {
            var accessType = eligibleMembership.Membership.IsUnlimited
                ? ReservationAccessType.UnlimitedMembership
                : ReservationAccessType.MembershipCredit;

            return new SessionEligibilityEvaluationDto(
                session.ClassSessionId,
                true,
                accessType,
                eligibleMembership.Membership.Id,
                null,
                eligibleMembership.Membership.AvailableCredits,
                Array.Empty<EligibilityIssueDto>());
        }

        // 2. Single class ticket for this session
        var ticket = accessMovements
            .FirstOrDefault(cm => cm.Type == CreditMovementType.SingleClassTicket &&
                                  cm.ClassSessionId == session.ClassSessionId &&
                                  !heldMovementIds.Contains(cm.Id));

        if (ticket != null)
        {
            var paymentsForSession = sessionPayments
                .Where(p => p.ClassSessionId == session.ClassSessionId)
                .ToList();

            var paymentConfirmed = paymentsForSession.Count == 0 ||
                                   paymentsForSession.Any(p => p.Status == PaymentStatus.Completed);

            if (paymentConfirmed)
            {
                return new SessionEligibilityEvaluationDto(
                    session.ClassSessionId,
                    true,
                    ReservationAccessType.SingleClassTicket,
                    null,
                    ticket.Id,
                    1,
                    Array.Empty<EligibilityIssueDto>());
            }
        }

        // 3. Autonomous compensatory credit valid on session date & activity scope (earliest ExpiresOn first)
        var compensatory = accessMovements
            .Where(cm => cm.Type == CreditMovementType.ClassSuspensionCompensation &&
                         !heldMovementIds.Contains(cm.Id) &&
                         (!cm.ExpiresOn.HasValue || cm.ExpiresOn.Value >= evalDate) &&
                         (!cm.ActivityId.HasValue || cm.ActivityId.Value == session.ActivityId))
            .OrderBy(cm => cm.ExpiresOn ?? DateOnly.MaxValue)
            .ThenBy(cm => cm.CreatedAtUtc)
            .ThenBy(cm => cm.Id)
            .FirstOrDefault();

        if (compensatory != null)
        {
            return new SessionEligibilityEvaluationDto(
                session.ClassSessionId,
                true,
                ReservationAccessType.CompensatoryCredit,
                null,
                compensatory.Id,
                1,
                Array.Empty<EligibilityIssueDto>());
        }

        // 4. Not eligible: report best membership ineligibility reasons or NO_AVAILABLE_CREDITS
        if (evaluatedMemberships.Count > 0)
        {
            var bestIneligible = evaluatedMemberships
                .OrderBy(x => GetCandidateStatusPriority(x.Eligibility.EffectiveStatus))
                .ThenBy(x => x.Membership.IsUnlimited ? 0 : 1)
                .ThenBy(x => x.Membership.EndDate)
                .ThenBy(x => x.Membership.PurchasedAtUtc)
                .ThenBy(x => x.Membership.Id)
                .First();

            return new SessionEligibilityEvaluationDto(
                session.ClassSessionId,
                false,
                null,
                null,
                null,
                0,
                bestIneligible.Eligibility.Issues);
        }

        return new SessionEligibilityEvaluationDto(
            session.ClassSessionId,
            false,
            null,
            null,
            null,
            0,
            new[]
            {
                new EligibilityIssueDto(
                    EligibilityIssueCodes.NoAvailableCredits,
                    "El alumno no posee ninguna membresía registrada, pase individual ni crédito compensatorio vigente.")
            });
    }

    private async Task<(DateOnly ResolvedDate, Guid? ResolvedActivityId, Activity? Activity)> ResolveContextAsync(
        DateOnly? targetDate,
        Guid? activityId,
        Guid? classSessionId,
        CancellationToken cancellationToken)
    {
        var resolvedDate = targetDate ?? _gymCalendar.Today;
        var resolvedActivityId = activityId;
        Activity? activity = null;

        if (classSessionId.HasValue)
        {
            var session = await _dbContext.ClassSessions
                .AsNoTracking()
                .Include(cs => cs.Activity)
                .FirstOrDefaultAsync(cs => cs.Id == classSessionId.Value && cs.IsActive, cancellationToken)
                ?? throw new NotFoundException("La clase especificada no existe o no está activa.");

            resolvedDate = targetDate ?? DateOnly.FromDateTime(_gymCalendar.ToLocal(session.StartsAtUtc).DateTime);
            resolvedActivityId = activityId ?? session.ActivityId;
            activity = session.Activity;
        }

        if (resolvedActivityId.HasValue && (activity == null || activity.Id != resolvedActivityId.Value))
        {
            activity = await _dbContext.Activities
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == resolvedActivityId.Value, cancellationToken);
        }

        return (resolvedDate, resolvedActivityId, activity);
    }

    private MembershipEligibilityDto EvaluateMembership(
        Membership membership,
        Person? student,
        DateOnly evalDate,
        Guid? activityId,
        Guid? classSessionId,
        Activity? activity)
    {
        var issues = new List<EligibilityIssueDto>();
        var today = _gymCalendar.Today;
        var effectiveStatus = membership.GetEffectiveStatus(today);

        // 1. STUDENT_NOT_ACTIVE
        if (!IsStudentActive(student))
        {
            issues.Add(new EligibilityIssueDto(
                EligibilityIssueCodes.StudentNotActive,
                "El alumno no se encuentra en estado activo o no tiene el rol de Alumno activo."));
        }

        // 2. MEMBERSHIP_PENDING
        if (membership.Status == MembershipStatus.Pending)
        {
            issues.Add(new EligibilityIssueDto(
                EligibilityIssueCodes.MembershipPending,
                "La membresía se encuentra pendiente de activación."));
        }

        // 3. MEMBERSHIP_CANCELLED
        if (membership.Status == MembershipStatus.Cancelled)
        {
            issues.Add(new EligibilityIssueDto(
                EligibilityIssueCodes.MembershipCancelled,
                "La membresía se encuentra cancelada."));
        }

        // 4. MEMBERSHIP_EXPIRED
        if (effectiveStatus == MembershipStatus.Expired)
        {
            issues.Add(new EligibilityIssueDto(
                EligibilityIssueCodes.MembershipExpired,
                "La membresía se encuentra vencida."));
        }
        // 5. OUT_OF_VALIDITY_RANGE
        else if (evalDate < membership.StartDate || evalDate > membership.EndDate)
        {
            issues.Add(new EligibilityIssueDto(
                EligibilityIssueCodes.OutOfValidityRange,
                $"La fecha consultada ({evalDate:yyyy-MM-dd}) está fuera de la vigencia de la membresía ({membership.StartDate:yyyy-MM-dd} al {membership.EndDate:yyyy-MM-dd})."));
        }

        // 6. MEMBERSHIP_SUSPENDED
        if (membership.Status == MembershipStatus.Suspended && effectiveStatus != MembershipStatus.Expired)
        {
            issues.Add(new EligibilityIssueDto(
                EligibilityIssueCodes.MembershipSuspended,
                "La membresía se encuentra suspendida temporalmente."));
        }

        // 7. ACTIVITY_NOT_ACTIVE & 8. ACTIVITY_NOT_ALLOWED
        if (activityId.HasValue)
        {
            if (activity == null || !activity.IsActive || activity.Status != ActivityStatus.Active)
            {
                issues.Add(new EligibilityIssueDto(
                    EligibilityIssueCodes.ActivityNotActive,
                    "La actividad consultada no existe o no se encuentra activa."));
            }

            if (!membership.AllowsActivity(activityId.Value))
            {
                issues.Add(new EligibilityIssueDto(
                    EligibilityIssueCodes.ActivityNotAllowed,
                    "Las condiciones contratadas de la membresía no incluyen la actividad seleccionada."));
            }
        }

        // 9. NO_AVAILABLE_CREDITS
        if (!membership.HasAvailableCredit())
        {
            issues.Add(new EligibilityIssueDto(
                EligibilityIssueCodes.NoAvailableCredits,
                "La membresía no posee créditos disponibles."));
        }

        // 10. AGE_NOT_ALLOWED
        if (activity != null && student?.BirthDate != null)
        {
            var birthDateOnly = DateOnly.FromDateTime(student.BirthDate.Value);
            var age = CalculateAge(birthDateOnly, evalDate);
            if (!activity.IsAgeAllowed(age))
            {
                issues.Add(new EligibilityIssueDto(
                    EligibilityIssueCodes.AgeNotAllowed,
                    $"La actividad '{activity.Name}' no admite alumnos de {age} años."));
            }
        }

        // 11. UNDERAGE_EMERGENCY_CONTACT_REQUIRED & MEDICAL_CERTIFICATE_REQUIRED
        if (student != null)
        {
            if (student.BirthDate.HasValue)
            {
                var birthDateOnly = DateOnly.FromDateTime(student.BirthDate.Value);
                var age = CalculateAge(birthDateOnly, evalDate);
                if (age < 18 && !student.HasValidEmergencyContact())
                {
                    issues.Add(new EligibilityIssueDto(
                        EligibilityIssueCodes.UnderageEmergencyContactRequired,
                        "El alumno menor de edad requiere un contacto de emergencia válido para acceder."));
                }
            }

            if (_gymOptions.RequireValidMedicalCertificateForAccess)
            {
                var hasValidCert = student.MedicalCertificateExpiration.HasValue &&
                                   DateOnly.FromDateTime(student.MedicalCertificateExpiration.Value) >= evalDate;
                if (!hasValidCert)
                {
                    issues.Add(new EligibilityIssueDto(
                        EligibilityIssueCodes.MedicalCertificateRequired,
                        "El gimnasio exige un certificado de aptitud médica vigente."));
                }
            }
        }

        var isUnlimited = membership.IsUnlimited;

        return new MembershipEligibilityDto(
            membership.Id,
            membership.StudentId,
            evalDate,
            activityId,
            classSessionId,
            membership.Status,
            effectiveStatus,
            isUnlimited,
            membership.AvailableCredits,
            membership.IsDepleted,
            issues.Count == 0,
            issues);
    }

    private static bool IsStudentActive(Person? student)
    {
        if (student == null || !student.IsActive || student.Status != PersonStatus.Active)
        {
            return false;
        }

        return student.PersonRoles.Any(pr => pr.IsActive && pr.Role != null && pr.Role.Name == RoleType.Student.ToString());
    }

    private static int CalculateAge(DateOnly birthDate, DateOnly referenceDate)
    {
        var age = referenceDate.Year - birthDate.Year;
        if (birthDate > referenceDate.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    private static int GetCandidateStatusPriority(MembershipStatus effectiveStatus) =>
        effectiveStatus switch
        {
            MembershipStatus.Active => 0,
            MembershipStatus.Suspended => 1,
            MembershipStatus.Pending => 2,
            MembershipStatus.Expired => 3,
            MembershipStatus.Cancelled => 4,
            _ => 5
        };
}
