using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Domain.Entities.Memberships;

namespace SmartGym.Application.Modules.Memberships.Services;

public class MembershipPlanService : IMembershipPlanService
{
    private readonly ISmartGymDbContext _dbContext;

    public MembershipPlanService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<MembershipPlanDto>> GetAllAsync(bool onlyActive = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.MembershipPlans
            .Include(p => p.AllowedActivities)
                .ThenInclude(pa => pa.Activity)
            .AsNoTracking();

        if (onlyActive)
        {
            query = query.Where(p => p.IsActive);
        }

        var list = await query
            .OrderBy(p => p.Price)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<MembershipPlanDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.MembershipPlans
            .Include(p => p.AllowedActivities)
                .ThenInclude(pa => pa.Activity)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return plan == null ? null : MapToDto(plan);
    }

    public async Task<MembershipPlanDto> CreateAsync(CreateMembershipPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("El nombre del plan no puede estar vacío.", nameof(request.Name));
        }

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            DurationDays = request.DurationDays,
            Price = request.Price,
            Credits = request.Credits,
            IsUnlimited = request.IsUnlimited,
            AppliesToAllActivities = request.AppliesToAllActivities,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        if (!request.AppliesToAllActivities && request.AllowedActivityIds != null)
        {
            foreach (var activityId in request.AllowedActivityIds)
            {
                plan.AddAllowedActivity(activityId);
            }
        }

        _dbContext.MembershipPlans.Add(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(plan.Id, cancellationToken) ?? MapToDto(plan);
    }

    public async Task<MembershipPlanDto?> UpdateAsync(Guid id, UpdateMembershipPlanRequest request, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.MembershipPlans
            .Include(p => p.AllowedActivities)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (plan == null) return null;

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("El nombre del plan no puede estar vacío.", nameof(request.Name));
        }

        plan.Name = request.Name.Trim();
        plan.Description = request.Description?.Trim();
        plan.DurationDays = request.DurationDays;
        plan.Price = request.Price;
        plan.Credits = request.Credits;
        plan.IsUnlimited = request.IsUnlimited;
        plan.AppliesToAllActivities = request.AppliesToAllActivities;
        plan.IsActive = request.IsActive;
        plan.UpdatedAtUtc = DateTime.UtcNow;

        plan.AllowedActivities.Clear();
        if (!request.AppliesToAllActivities && request.AllowedActivityIds != null)
        {
            foreach (var activityId in request.AllowedActivityIds)
            {
                plan.AddAllowedActivity(activityId);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(plan.Id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.MembershipPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan == null) return false;

        plan.IsActive = false;
        plan.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static MembershipPlanDto MapToDto(MembershipPlan p) =>
        new(
            p.Id,
            p.Name,
            p.Description,
            p.DurationDays,
            p.Price,
            p.Credits,
            p.IsUnlimited,
            p.AppliesToAllActivities,
            p.AllowedActivities.Select(pa => pa.ActivityId).ToList(),
            p.AllowedActivities.Select(pa => pa.Activity?.Name ?? string.Empty).Where(n => !string.IsNullOrEmpty(n)).ToList(),
            p.IsActive
        );
}
