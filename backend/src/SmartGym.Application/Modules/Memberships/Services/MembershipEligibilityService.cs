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
