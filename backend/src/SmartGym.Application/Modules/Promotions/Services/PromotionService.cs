using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Promotions.Services;

public class PromotionService : IPromotionService
{
    private readonly ISmartGymDbContext _dbContext;

    public PromotionService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<PromotionDto>> GetAllAsync(bool onlyActive = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Promotions
            .Include(p => p.ApplicablePlan)
            .AsNoTracking();

        if (onlyActive)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            query = query.Where(p => p.IsActive && p.ValidFrom <= today && (!p.ValidTo.HasValue || today <= p.ValidTo.Value));
        }

        var list = await query
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<PromotionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promo = await _dbContext.Promotions
            .Include(p => p.ApplicablePlan)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return promo == null ? null : MapToDto(promo);
    }

    public async Task<PromotionDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var promo = await _dbContext.Promotions
            .Include(p => p.ApplicablePlan)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code.ToUpper() == code.Trim().ToUpper(), cancellationToken);

        return promo == null ? null : MapToDto(promo);
    }

    public async Task<PromotionDto> CreateAsync(CreatePromotionRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim().ToUpper();
        var exists = await _dbContext.Promotions.AnyAsync(p => p.Code.ToUpper() == code, cancellationToken);
        if (exists)
        {
            throw new ArgumentException($"Ya existe una promoción con el código '{code}'.");
        }

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ConditionType = request.ConditionType,
            BenefitType = request.BenefitType,
            BenefitValue = request.BenefitValue,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            MaxUsesTotal = request.MaxUsesTotal,
            MaxUsesPerPerson = request.MaxUsesPerPerson > 0 ? request.MaxUsesPerPerson : 1,
            ApplicablePlanId = request.ApplicablePlanId,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.Promotions.Add(promo);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(promo);
    }

    public async Task<PromotionApplicationResultDto> EvaluatePromotionAsync(ApplyPromotionRequest request, CancellationToken cancellationToken = default)
    {
        var promo = await _dbContext.Promotions
            .FirstOrDefaultAsync(p => p.Code.ToUpper() == request.Code.Trim().ToUpper() && p.IsActive, cancellationToken);

        if (promo == null)
        {
            throw new ArgumentException("El código promocional especificado no existe o no se encuentra activo.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!promo.IsValidOn(today))
        {
            throw new InvalidOperationException("La promoción está vencida o ha superado el tope total de canjes permitidos.");
        }

        if (promo.ApplicablePlanId.HasValue && request.MembershipPlanId.HasValue && promo.ApplicablePlanId.Value != request.MembershipPlanId.Value)
        {
            throw new InvalidOperationException("La promoción no es aplicable para el plan seleccionado.");
        }

        var person = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == request.PersonId && p.IsActive, cancellationToken);
        if (person == null)
        {
            throw new ArgumentException("El alumno o persona especificada no existe.");
        }

        // Validación de condiciones
        if (promo.ConditionType == PromotionConditionType.NewStudent)
        {
            var hasMemberships = await _dbContext.Memberships.AnyAsync(m => m.StudentId == person.Id, cancellationToken);
            if (hasMemberships)
            {
                throw new InvalidOperationException("Esta promoción es de bienvenida y aplica exclusivamente a nuevos alumnos.");
            }
        }
        else if (promo.ConditionType == PromotionConditionType.FamilyGroup)
        {
            var isFamilyMember = await _dbContext.FamilyGroupMembers.AnyAsync(m => m.PersonId == person.Id && m.IsActive, cancellationToken);
            if (!isFamilyMember)
            {
                throw new InvalidOperationException("Esta promoción requiere pertenecer a un grupo familiar activo.");
            }
        }

        // Validación de tope de uso por persona
        var previousUses = await _dbContext.PromotionRedemptions
            .CountAsync(r => r.PromotionId == promo.Id && r.PersonId == person.Id, cancellationToken);

        if (!promo.CanBeUsedBy(person.Id, previousUses))
        {
            throw new InvalidOperationException($"Has alcanzado el límite de usos permitidos ({promo.MaxUsesPerPerson}) para esta promoción.");
        }

        var discount = promo.CalculateDiscount(request.BasePrice);
        var finalAmount = Math.Max(0m, request.BasePrice - discount);
        var extraCredits = promo.GetExtraCredits();

        return new PromotionApplicationResultDto(
            promo.Id,
            promo.Code,
            promo.Name,
            request.BasePrice,
            discount,
            finalAmount,
            extraCredits
        );
    }

    public async Task<PromotionRedemptionDto> RedeemPromotionAsync(
        ApplyPromotionRequest request,
        Guid? membershipId,
        CancellationToken cancellationToken = default)
    {
        var eval = await EvaluatePromotionAsync(request, cancellationToken);

        var promo = await _dbContext.Promotions.FirstAsync(p => p.Id == eval.PromotionId, cancellationToken);
        var person = await _dbContext.People.FirstAsync(p => p.Id == request.PersonId, cancellationToken);

        var redemption = new PromotionRedemption
        {
            Id = Guid.NewGuid(),
            PromotionId = promo.Id,
            PersonId = person.Id,
            MembershipId = membershipId,
            OriginalAmount = eval.OriginalAmount,
            DiscountAmount = eval.DiscountAmount,
            FinalAmount = eval.FinalAmount,
            ExtraCreditsGranted = eval.ExtraCreditsGranted,
            RedeemedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        promo.IncrementUse();
        _dbContext.PromotionRedemptions.Add(redemption);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PromotionRedemptionDto(
            redemption.Id,
            promo.Id,
            promo.Code,
            person.Id,
            person.FullName,
            redemption.OriginalAmount,
            redemption.DiscountAmount,
            redemption.FinalAmount,
            redemption.ExtraCreditsGranted,
            redemption.RedeemedAtUtc
        );
    }

    public async Task<List<PromotionRedemptionDto>> GetRedemptionsByPersonAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.PromotionRedemptions
            .Include(r => r.Promotion)
            .Include(r => r.Person)
            .AsNoTracking()
            .Where(r => r.PersonId == personId)
            .OrderByDescending(r => r.RedeemedAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(r => new PromotionRedemptionDto(
            r.Id,
            r.PromotionId,
            r.Promotion?.Code ?? string.Empty,
            r.PersonId,
            r.Person?.FullName ?? string.Empty,
            r.OriginalAmount,
            r.DiscountAmount,
            r.FinalAmount,
            r.ExtraCreditsGranted,
            r.RedeemedAtUtc
        )).ToList();
    }

    private static PromotionDto MapToDto(Promotion p) =>
        new(
            p.Id,
            p.Code,
            p.Name,
            p.Description,
            p.ConditionType,
            p.BenefitType,
            p.BenefitValue,
            p.ValidFrom,
            p.ValidTo,
            p.MaxUsesTotal,
            p.CurrentUsesTotal,
            p.MaxUsesPerPerson,
            p.ApplicablePlanId,
            p.ApplicablePlan?.Name,
            p.IsActive
        );
}
