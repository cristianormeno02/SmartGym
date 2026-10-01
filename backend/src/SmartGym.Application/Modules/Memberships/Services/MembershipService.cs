using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Memberships.Services;

public class MembershipService : IMembershipService
{
    private readonly ISmartGymDbContext _dbContext;

    public MembershipService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<MembershipDto> AssignMembershipAsync(AssignMembershipRequest request, CancellationToken cancellationToken = default)
    {
        var student = await _dbContext.People
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == request.StudentId && p.IsActive, cancellationToken);

        if (student == null || !student.PersonRoles.Any(pr => pr.IsActive && pr.Role.Name == RoleType.Student.ToString()))
        {
            throw new ArgumentException("El alumno especificado no existe o no tiene el rol de Alumno activo.");
        }

        var plan = await _dbContext.MembershipPlans
            .FirstOrDefaultAsync(p => p.Id == request.MembershipPlanId && p.IsActive, cancellationToken);

        if (plan == null)
        {
            throw new ArgumentException("El plan de membresía especificado no existe o no está activo.");
        }

        var startDate = request.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var endDate = startDate.AddDays(plan.DurationDays);

        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = request.StudentId,
            MembershipPlanId = request.MembershipPlanId,
            StartDate = startDate,
            EndDate = endDate,
            TotalCredits = plan.Credits,
            AvailableCredits = plan.Credits,
            Status = MembershipStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        var initialMovement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            MembershipId = membership.Id,
            StudentId = membership.StudentId,
            Type = CreditMovementType.InitialPurchase,
            Amount = plan.IsUnlimited ? 0 : plan.Credits,
            BalanceAfter = plan.Credits,
            Reason = $"Adquisición inicial de plan: {plan.Name}",
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.Memberships.Add(membership);
        _dbContext.MembershipCreditMovements.Add(initialMovement);
        await _dbContext.SaveChangesAsync(cancellationToken);

        membership.Student = student;
        membership.MembershipPlan = plan;

        return MapToDto(membership);
    }

    public async Task<List<MembershipDto>> GetByStudentIdAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.Memberships
            .Include(m => m.Student)
            .Include(m => m.MembershipPlan)
            .AsNoTracking()
            .Where(m => m.StudentId == studentId && m.IsActive)
            .OrderByDescending(m => m.StartDate)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<MembershipDto?> GetActiveMembershipAsync(Guid studentId, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var membership = await _dbContext.Memberships
            .Include(m => m.Student)
            .Include(m => m.MembershipPlan)
            .AsNoTracking()
            .Where(m => m.StudentId == studentId && m.IsActive && m.Status == MembershipStatus.Active)
            .Where(m => m.StartDate <= targetDate && targetDate <= m.EndDate)
            .OrderByDescending(m => m.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        return membership == null ? null : MapToDto(membership);
    }

    public async Task<MembershipDto?> CancelMembershipAsync(Guid membershipId, string reason, CancellationToken cancellationToken = default)
    {
        var membership = await _dbContext.Memberships
            .Include(m => m.Student)
            .Include(m => m.MembershipPlan)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.IsActive, cancellationToken);

        if (membership == null) return null;

        membership.Status = MembershipStatus.Cancelled;
        membership.UpdatedAtUtc = DateTime.UtcNow;

        var movement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            MembershipId = membership.Id,
            StudentId = membership.StudentId,
            Type = CreditMovementType.ManualAdjustment,
            Amount = -membership.AvailableCredits,
            BalanceAfter = 0,
            Reason = $"Cancelación de membresía: {reason}",
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        membership.AvailableCredits = 0;
        _dbContext.MembershipCreditMovements.Add(movement);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(membership);
    }

    private static MembershipDto MapToDto(Membership m)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new MembershipDto(
            m.Id,
            m.StudentId,
            m.Student?.FullName ?? string.Empty,
            m.Student?.Dni ?? string.Empty,
            m.MembershipPlanId,
            m.MembershipPlan?.Name ?? string.Empty,
            m.StartDate,
            m.EndDate,
            m.TotalCredits,
            m.AvailableCredits,
            m.Status,
            m.IsValidOn(today),
            m.Student?.Document?.Type,
            m.Student?.Document?.Number ?? m.Student?.Dni
        );
    }
}
