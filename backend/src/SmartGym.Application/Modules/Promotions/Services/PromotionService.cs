using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Promotions.Services;

public class PromotionService : IPromotionService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly SmartGym.Application.Common.IGymCalendar _gymCalendar;

    public PromotionService(
        ISmartGymDbContext dbContext,
        SmartGym.Application.Common.IGymCalendar? gymCalendar = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar ?? new SmartGym.Application.Common.GymCalendar(
            Microsoft.Extensions.Options.Options.Create(new SmartGym.Application.Common.GymOptions()));
    }

    public async Task<List<PromotionDto>> GetAllAsync(bool onlyActive = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Promotions
            .Include(p => p.ApplicablePlan)
            .AsNoTracking();

        if (onlyActive)
        {
            var today = _gymCalendar.Today;
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
            CreatedAtUtc = _gymCalendar.UtcNow,
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

        var today = _gymCalendar.Today;
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
            var hasMemberships = await _dbContext.Memberships
                .AnyAsync(m => m.StudentId == person.Id && m.IsActive && m.Status != MembershipStatus.Cancelled, cancellationToken);
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

        // Validación de tope de uso por persona (sólo canjes activos)
        var previousUses = await _dbContext.PromotionRedemptions
            .CountAsync(r => r.PromotionId == promo.Id && r.PersonId == person.Id && r.IsActive, cancellationToken);

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

        var nowUtc = _gymCalendar.UtcNow;
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
            RedeemedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
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
            redemption.RedeemedAtUtc,
            redemption.ReservationId
        );
    }

    public async Task<PromotionApplicationResultDto> EvaluatePromotionForReservationAsync(
        Guid studentId,
        Guid classSessionId,
        Guid activityId,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw ConflictException.WithCode("PROMOTION_NOT_APPLICABLE", "El código promocional es obligatorio.");
        }

        var normalizedCode = code.Trim().ToUpper();
        var promo = await _dbContext.Promotions
            .FirstOrDefaultAsync(p => p.Code.ToUpper() == normalizedCode && p.IsActive, cancellationToken);

        if (promo == null)
        {
            throw ConflictException.WithCode("PROMOTION_NOT_APPLICABLE", "El código promocional especificado no existe o no se encuentra activo.");
        }

        var today = _gymCalendar.Today;
        var evalDate = today;

        if (classSessionId != Guid.Empty)
        {
            var session = await _dbContext.ClassSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(cs => cs.Id == classSessionId && cs.IsActive, cancellationToken);
            if (session != null)
            {
                evalDate = DateOnly.FromDateTime(_gymCalendar.ToLocal(session.StartsAtUtc).DateTime);
            }
        }

        if (!promo.IsValidOn(today) || !promo.IsValidOn(evalDate))
        {
            throw ConflictException.WithCode("PROMOTION_NOT_APPLICABLE", "La promoción está vencida, fuera de vigencia o ha superado el tope total de canjes permitidos.");
        }

        if (promo.ApplicablePlanId.HasValue)
        {
            throw ConflictException.WithCode("PROMOTION_NOT_APPLICABLE", "La promoción es exclusiva para la contratación de un plan de membresía.");
        }

        var coversFullAccess =
            (promo.BenefitType == PromotionBenefitType.PercentageDiscount && promo.BenefitValue >= 100m) ||
            (promo.BenefitType == PromotionBenefitType.ExtraCredits && promo.BenefitValue >= 1m);

        if (!coversFullAccess)
        {
            throw ConflictException.WithCode("PROMOTION_NOT_APPLICABLE", "La promoción no cubre el acceso completo a la clase.");
        }

        var person = await _dbContext.People
            .FirstOrDefaultAsync(p => p.Id == studentId && p.IsActive, cancellationToken);
        if (person == null)
        {
            throw ConflictException.WithCode("PROMOTION_NOT_APPLICABLE", "El alumno especificado no existe o no se encuentra activo.");
        }

        if (promo.ConditionType == PromotionConditionType.NewStudent)
        {
            var hasMemberships = await _dbContext.Memberships
                .AnyAsync(m => m.StudentId == person.Id && m.IsActive && m.Status != MembershipStatus.Cancelled, cancellationToken);
            if (hasMemberships)
            {
                throw ConflictException.WithCode("PROMOTION_NOT_APPLICABLE", "Esta promoción es de bienvenida y aplica exclusivamente a nuevos alumnos.");
            }
        }
        else if (promo.ConditionType == PromotionConditionType.FamilyGroup)
        {
            var isFamilyMember = await _dbContext.FamilyGroupMembers
                .AnyAsync(m => m.PersonId == person.Id && m.IsActive, cancellationToken);
            if (!isFamilyMember)
            {
                throw ConflictException.WithCode("PROMOTION_NOT_APPLICABLE", "Esta promoción requiere pertenecer a un grupo familiar activo.");
            }
        }

        var previousUses = await _dbContext.PromotionRedemptions
            .CountAsync(r => r.PromotionId == promo.Id && r.PersonId == person.Id && r.IsActive, cancellationToken);

        if (!promo.CanBeUsedBy(person.Id, previousUses))
        {
            throw ConflictException.WithCode("PROMOTION_NOT_APPLICABLE", $"Has alcanzado el límite de usos permitidos ({promo.MaxUsesPerPerson}) para esta promoción.");
        }

        var extraCredits = promo.GetExtraCredits();

        return new PromotionApplicationResultDto(
            promo.Id,
            promo.Code,
            promo.Name,
            0m,
            0m,
            0m,
            extraCredits
        );
    }

    public async Task<PromotionRedemptionDto> RedeemPromotionForReservationAsync(
        Guid studentId,
        Guid classSessionId,
        Guid activityId,
        string code,
        Guid reservationId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.PromotionRedemptions
            .Include(r => r.Promotion)
            .Include(r => r.Person)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId && r.IsActive, cancellationToken);

        if (existing != null)
        {
            return new PromotionRedemptionDto(
                existing.Id,
                existing.PromotionId,
                existing.Promotion?.Code ?? code.Trim().ToUpper(),
                existing.PersonId,
                existing.Person?.FullName ?? string.Empty,
                existing.OriginalAmount,
                existing.DiscountAmount,
                existing.FinalAmount,
                existing.ExtraCreditsGranted,
                existing.RedeemedAtUtc,
                existing.ReservationId
            );
        }

        var eval = await EvaluatePromotionForReservationAsync(studentId, classSessionId, activityId, code, cancellationToken);

        var promo = await _dbContext.Promotions.FirstAsync(p => p.Id == eval.PromotionId, cancellationToken);
        var person = await _dbContext.People.FirstAsync(p => p.Id == studentId, cancellationToken);

        var nowUtc = _gymCalendar.UtcNow;
        var redemption = new PromotionRedemption
        {
            Id = Guid.NewGuid(),
            PromotionId = promo.Id,
            PersonId = person.Id,
            MembershipId = null,
            ReservationId = reservationId,
            OriginalAmount = eval.OriginalAmount,
            DiscountAmount = eval.DiscountAmount,
            FinalAmount = eval.FinalAmount,
            ExtraCreditsGranted = eval.ExtraCreditsGranted,
            RedeemedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            IsActive = true
        };

        promo.IncrementUse();
        _dbContext.PromotionRedemptions.Add(redemption);

        if (_dbContext.Database.CurrentTransaction == null)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

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
            redemption.RedeemedAtUtc,
            redemption.ReservationId
        );
    }

    public async Task AttachRedemptionToMembershipAsync(
        Guid redemptionId,
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        var redemption = await _dbContext.PromotionRedemptions
            .FirstOrDefaultAsync(r => r.Id == redemptionId, cancellationToken)
            ?? throw new NotFoundException("El canje promocional especificado no existe.");

        redemption.MembershipId = membershipId;
        redemption.UpdatedAtUtc = _gymCalendar.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task VoidRedemptionAsync(
        Guid redemptionId,
        CancellationToken cancellationToken = default)
    {
        var redemption = await _dbContext.PromotionRedemptions
            .Include(r => r.Promotion)
            .FirstOrDefaultAsync(r => r.Id == redemptionId, cancellationToken)
            ?? throw new NotFoundException("El canje promocional especificado no existe.");

        if (!redemption.IsActive)
        {
            return;
        }

        redemption.IsActive = false;
        redemption.UpdatedAtUtc = _gymCalendar.UtcNow;
        redemption.Promotion?.DecrementUse();

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<PromotionRedemptionDto>> GetRedemptionsByPersonAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.PromotionRedemptions
            .Include(r => r.Promotion)
            .Include(r => r.Person)
            .AsNoTracking()
            .Where(r => r.PersonId == personId && r.IsActive)
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
            r.RedeemedAtUtc,
            r.ReservationId
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
