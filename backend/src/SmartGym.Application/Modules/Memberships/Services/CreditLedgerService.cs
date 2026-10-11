using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Memberships.Services;

public class CreditLedgerService : ICreditLedgerService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly SmartGym.Application.Common.IGymCalendar _gymCalendar;
    private readonly SmartGym.Application.Common.MembershipsOptions _options;

    public CreditLedgerService(
        ISmartGymDbContext dbContext,
        SmartGym.Application.Common.IGymCalendar? gymCalendar = null,
        Microsoft.Extensions.Options.IOptions<SmartGym.Application.Common.MembershipsOptions>? options = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar ?? new SmartGym.Application.Common.GymCalendar(
            Microsoft.Extensions.Options.Options.Create(new SmartGym.Application.Common.GymOptions()));
        _options = options?.Value ?? new SmartGym.Application.Common.MembershipsOptions();
    }

    public async Task<List<CreditMovementDto>> GetMovementsByStudentAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.MembershipCreditMovements
            .Include(cm => cm.Student)
            .Include(cm => cm.ClassSession)
                .ThenInclude(cs => cs!.Activity)
            .AsNoTracking()
            .Where(cm => cm.StudentId == studentId)
            .OrderByDescending(cm => cm.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<List<CreditMovementDto>> GetMovementsByMembershipAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.MembershipCreditMovements
            .Include(cm => cm.Student)
            .Include(cm => cm.ClassSession)
                .ThenInclude(cs => cs!.Activity)
            .AsNoTracking()
            .Where(cm => cm.MembershipId == membershipId)
            .OrderByDescending(cm => cm.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<MembershipCreditsSummaryDto> GetMembershipCreditsSummaryAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        var membership = await _dbContext.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.IsActive, cancellationToken);

        if (membership == null)
        {
            throw new NotFoundException("La membresía especificada no existe.");
        }

        var movements = await _dbContext.MembershipCreditMovements
            .Include(cm => cm.Student)
            .Include(cm => cm.ClassSession)
                .ThenInclude(cs => cs!.Activity)
            .AsNoTracking()
            .Where(cm => cm.MembershipId == membershipId && cm.IsActive)
            .OrderByDescending(cm => cm.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var initialCredits = movements
            .Where(m => m.Type == CreditMovementType.InitialPurchase)
            .Sum(m => m.Amount);

        var promotionBonusCredits = movements
            .Where(m => m.Type == CreditMovementType.PromotionBonus)
            .Sum(m => m.Amount);

        var compensatoryCredits = movements
            .Where(m => m.Type == CreditMovementType.ClassSuspensionCompensation)
            .Sum(m => m.Amount);

        var manualAdjustmentCredits = movements
            .Where(m => m.Type == CreditMovementType.ManualAdjustment)
            .Sum(m => m.Amount);

        var consumedCredits = movements
            .Where(m => m.Type == CreditMovementType.ClassReservationDebit)
            .Sum(m => Math.Abs(m.Amount));

        var refundedCredits = movements
            .Where(m => m.Type == CreditMovementType.ClassCancellationRefund)
            .Sum(m => m.Amount);

        var expiredOrForfeitedCredits = movements
            .Where(m => m.Type == CreditMovementType.Expiration || m.Type == CreditMovementType.CancellationForfeiture)
            .Sum(m => Math.Abs(m.Amount));

        return new MembershipCreditsSummaryDto(
            membership.Id,
            membership.StudentId,
            membership.IsUnlimited,
            initialCredits,
            promotionBonusCredits,
            compensatoryCredits,
            manualAdjustmentCredits,
            consumedCredits,
            RefundedCredits: refundedCredits,
            ExpiredOrForfeitedCredits: expiredOrForfeitedCredits,
            AvailableCredits: membership.AvailableCredits,
            IsDepleted: membership.IsDepleted,
            Movements: movements.Select(MapToDto).ToList()
        );
    }

    public Task RecordInitialCreditsAsync(
        Membership membership,
        Guid? performedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);

        if (membership.InitialCreditsLoaded)
        {
            return Task.CompletedTask;
        }

        var now = _gymCalendar.UtcNow;
        var planName = membership.MembershipPlan?.Name ?? "Plan contratado";

        if (membership.IsUnlimited)
        {
            membership.TotalCredits = 0;
            membership.AvailableCredits = 0;
            membership.InitialCreditsLoaded = true;
            membership.UpdatedAtUtc = now;

            var unlimitedMovement = new MembershipCreditMovement
            {
                Id = Guid.NewGuid(),
                MembershipId = membership.Id,
                StudentId = membership.StudentId,
                Type = CreditMovementType.InitialPurchase,
                Amount = 0,
                BalanceAfter = 0,
                Reason = $"Activación de plan ilimitado: {planName}",
                PerformedByUserId = performedByUserId,
                CreatedAtUtc = now,
                IsActive = true
            };

            membership.CreditMovements.Add(unlimitedMovement);
            _dbContext.MembershipCreditMovements.Add(unlimitedMovement);
            return Task.CompletedTask;
        }

        var initialCredits = Math.Max(0, membership.InitialCredits);
        var extraCredits = Math.Max(0, membership.ExtraCreditsGranted);

        membership.AvailableCredits += initialCredits;
        membership.TotalCredits += initialCredits;

        var initialMovement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            MembershipId = membership.Id,
            StudentId = membership.StudentId,
            Type = CreditMovementType.InitialPurchase,
            Amount = initialCredits,
            BalanceAfter = membership.AvailableCredits,
            Reason = $"Carga inicial de créditos: {planName}",
            PerformedByUserId = performedByUserId,
            CreatedAtUtc = now,
            IsActive = true
        };

        membership.CreditMovements.Add(initialMovement);
        _dbContext.MembershipCreditMovements.Add(initialMovement);

        if (extraCredits > 0)
        {
            membership.AvailableCredits += extraCredits;
            membership.TotalCredits += extraCredits;

            var bonusMovement = new MembershipCreditMovement
            {
                Id = Guid.NewGuid(),
                MembershipId = membership.Id,
                StudentId = membership.StudentId,
                Type = CreditMovementType.PromotionBonus,
                Amount = extraCredits,
                BalanceAfter = membership.AvailableCredits,
                Reason = $"Bonificación promocional de créditos ({extraCredits})",
                PerformedByUserId = performedByUserId,
                CreatedAtUtc = now.AddMilliseconds(1),
                IsActive = true
            };

            membership.CreditMovements.Add(bonusMovement);
            _dbContext.MembershipCreditMovements.Add(bonusMovement);
        }

        membership.InitialCreditsLoaded = true;
        membership.UpdatedAtUtc = now;
        return Task.CompletedTask;
    }

    public async Task<MembershipCreditMovement> ConsumeCreditForReservationAsync(
        Membership membership,
        Guid classSessionId,
        DateOnly sessionLocalDate,
        string reason,
        Guid? performedByUserId = null,
        Guid? reservationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);

        if (!membership.IsValidOn(sessionLocalDate))
        {
            throw new InvalidOperationException("La membresía no está activa en la fecha de la clase indicada.");
        }

        if (!membership.HasAvailableCredit())
        {
            throw new InvalidOperationException("No posee créditos disponibles en su membresía.");
        }

        if (reservationId.HasValue)
        {
            var alreadyDebitedForReservation = membership.CreditMovements.Any(
                    cm => cm.ReservationId == reservationId.Value && cm.Type == CreditMovementType.ClassReservationDebit && cm.IsActive)
                || await _dbContext.MembershipCreditMovements.AnyAsync(
                    cm => cm.ReservationId == reservationId.Value && cm.Type == CreditMovementType.ClassReservationDebit && cm.IsActive,
                    cancellationToken);

            if (alreadyDebitedForReservation)
            {
                throw new ConflictException("Ya existe un débito de crédito registrado para esta reserva.");
            }
        }

        // Prevent double consumption for the same (MembershipId, ClassSessionId) when net debits > refunds
        var existingMovements = await _dbContext.MembershipCreditMovements
            .Where(cm => cm.MembershipId == membership.Id && cm.ClassSessionId == classSessionId && cm.IsActive)
            .ToListAsync(cancellationToken);

        var debitsCount = existingMovements.Count(cm => cm.Type == CreditMovementType.ClassReservationDebit);
        var refundsCount = existingMovements.Count(cm => cm.Type == CreditMovementType.ClassCancellationRefund);
        if (debitsCount > refundsCount)
        {
            throw new ConflictException("Ya existe un consumo de crédito activo para esta membresía y clase.");
        }

        var isUnlimited = membership.IsUnlimited;
        if (!isUnlimited)
        {
            membership.AvailableCredits--;
        }

        var now = _gymCalendar.UtcNow;
        membership.UpdatedAtUtc = now;

        var movement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            MembershipId = membership.Id,
            StudentId = membership.StudentId,
            ClassSessionId = classSessionId,
            ReservationId = reservationId,
            Type = CreditMovementType.ClassReservationDebit,
            Amount = isUnlimited ? 0 : -1,
            BalanceAfter = membership.AvailableCredits,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Reserva de clase" : reason.Trim(),
            PerformedByUserId = performedByUserId,
            CreatedAtUtc = now,
            IsActive = true
        };

        membership.CreditMovements.Add(movement);
        _dbContext.MembershipCreditMovements.Add(movement);
        return movement;
    }

    public async Task<MembershipCreditMovement> RefundCreditForReservationAsync(
        Membership membership,
        Guid classSessionId,
        string reason,
        Guid? performedByUserId = null,
        Guid? reservationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);

        if (reservationId.HasValue)
        {
            var existingRefund = membership.CreditMovements.FirstOrDefault(
                    cm => cm.ReservationId == reservationId.Value && cm.Type == CreditMovementType.ClassCancellationRefund && cm.IsActive)
                ?? await _dbContext.MembershipCreditMovements.FirstOrDefaultAsync(
                    cm => cm.ReservationId == reservationId.Value && cm.Type == CreditMovementType.ClassCancellationRefund && cm.IsActive,
                    cancellationToken);

            if (existingRefund != null)
            {
                return existingRefund;
            }
        }

        var isUnlimited = membership.IsUnlimited;
        if (!isUnlimited)
        {
            membership.AvailableCredits++;
        }

        var now = _gymCalendar.UtcNow;
        membership.UpdatedAtUtc = now;

        var movement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            MembershipId = membership.Id,
            StudentId = membership.StudentId,
            ClassSessionId = classSessionId,
            ReservationId = reservationId,
            Type = CreditMovementType.ClassCancellationRefund,
            Amount = isUnlimited ? 0 : 1,
            BalanceAfter = membership.AvailableCredits,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Devolución por cancelación de reserva" : reason.Trim(),
            PerformedByUserId = performedByUserId,
            CreatedAtUtc = now,
            IsActive = true
        };

        membership.CreditMovements.Add(movement);
        _dbContext.MembershipCreditMovements.Add(movement);
        return movement;
    }

    public Task<MembershipCreditMovement?> ForfeitRemainingCreditsOnCancelAsync(
        Membership membership,
        string reason,
        Guid? performedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);

        var isUnlimited = membership.IsUnlimited;
        if (isUnlimited || membership.AvailableCredits <= 0)
        {
            membership.AvailableCredits = 0;
            return Task.FromResult<MembershipCreditMovement?>(null);
        }

        var forfeited = membership.AvailableCredits;
        membership.AvailableCredits = 0;

        var now = _gymCalendar.UtcNow;
        membership.UpdatedAtUtc = now;

        var movement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            MembershipId = membership.Id,
            StudentId = membership.StudentId,
            Type = CreditMovementType.CancellationForfeiture,
            Amount = -forfeited,
            BalanceAfter = 0,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Baja de créditos remanentes por cancelación" : reason.Trim(),
            PerformedByUserId = performedByUserId,
            CreatedAtUtc = now,
            IsActive = true
        };

        membership.CreditMovements.Add(movement);
        _dbContext.MembershipCreditMovements.Add(movement);
        return Task.FromResult<MembershipCreditMovement?>(movement);
    }

    public Task<MembershipCreditMovement?> ExpireRemainingCreditsAsync(
        Membership membership,
        string reason = "Expiración de créditos por finalización de vigencia",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);

        var isUnlimited = membership.IsUnlimited;
        if (isUnlimited || membership.AvailableCredits <= 0)
        {
            membership.AvailableCredits = 0;
            return Task.FromResult<MembershipCreditMovement?>(null);
        }

        var expired = membership.AvailableCredits;
        membership.AvailableCredits = 0;

        var now = _gymCalendar.UtcNow;
        membership.UpdatedAtUtc = now;

        var movement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            MembershipId = membership.Id,
            StudentId = membership.StudentId,
            Type = CreditMovementType.Expiration,
            Amount = -expired,
            BalanceAfter = 0,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Expiración de créditos por finalización de vigencia" : reason.Trim(),
            PerformedByUserId = null,
            CreatedAtUtc = now,
            IsActive = true
        };

        membership.CreditMovements.Add(movement);
        _dbContext.MembershipCreditMovements.Add(movement);
        return Task.FromResult<MembershipCreditMovement?>(movement);
    }

    public async Task<CreditMovementDto> AddCompensatoryCreditAsync(
        AddCompensatoryCreditRequest request,
        Guid performedByUserId,
        CancellationToken cancellationToken = default)
    {
        var student = await _dbContext.People
            .FirstOrDefaultAsync(p => p.Id == request.StudentId && p.IsActive, cancellationToken);

        if (student == null)
        {
            throw new ArgumentException("El alumno especificado no existe o no está activo.");
        }

        var today = _gymCalendar.Today;
        var activeMembership = await _dbContext.Memberships
            .Include(m => m.MembershipPlan)
            .Where(m => m.StudentId == request.StudentId && m.IsActive)
            .Where(m => m.Status == MembershipStatus.Active && m.EndDate >= today)
            .OrderByDescending(m => m.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        var now = _gymCalendar.UtcNow;
        MembershipCreditMovement movement;

        if (activeMembership != null)
        {
            if (!activeMembership.IsUnlimited)
            {
                activeMembership.AvailableCredits++;
                activeMembership.TotalCredits++;
            }
            activeMembership.UpdatedAtUtc = now;

            movement = new MembershipCreditMovement
            {
                Id = Guid.NewGuid(),
                MembershipId = activeMembership.Id,
                StudentId = request.StudentId,
                ClassSessionId = request.ClassSessionId,
                Type = CreditMovementType.ClassSuspensionCompensation,
                Amount = activeMembership.IsUnlimited ? 0 : 1,
                BalanceAfter = activeMembership.AvailableCredits,
                Reason = request.Reason,
                PerformedByUserId = performedByUserId,
                CreatedAtUtc = now,
                IsActive = true
            };

            activeMembership.CreditMovements.Add(movement);
            _dbContext.MembershipCreditMovements.Add(movement);
        }
        else
        {
            var validityDays = Math.Max(1, _options.CompensatoryCreditValidityDays);
            movement = new MembershipCreditMovement
            {
                Id = Guid.NewGuid(),
                StudentId = request.StudentId,
                ClassSessionId = request.ClassSessionId,
                ActivityId = request.ActivityId,
                ExpiresOn = today.AddDays(validityDays),
                Type = CreditMovementType.ClassSuspensionCompensation,
                Amount = 1,
                BalanceAfter = 1,
                Reason = request.Reason,
                PerformedByUserId = performedByUserId,
                CreatedAtUtc = now,
                IsActive = true
            };
            _dbContext.MembershipCreditMovements.Add(movement);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        movement.Student = student;
        return MapToDto(movement);
    }

    public async Task<CreditMovementDto> PurchaseSingleClassTicketAsync(
        PurchaseSingleClassTicketRequest request,
        Guid performedByUserId,
        CancellationToken cancellationToken = default)
    {
        var student = await _dbContext.People
            .FirstOrDefaultAsync(p => p.Id == request.StudentId && p.IsActive, cancellationToken);

        if (student == null)
        {
            throw new ArgumentException("El alumno especificado no existe o no está activo.");
        }

        var session = await _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .FirstOrDefaultAsync(cs => cs.Id == request.ClassSessionId && cs.IsActive, cancellationToken);

        if (session == null)
        {
            throw new ArgumentException("La clase especificada no existe o no está activa.");
        }

        var movement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = request.StudentId,
            ClassSessionId = request.ClassSessionId,
            Type = CreditMovementType.SingleClassTicket,
            Amount = 1,
            BalanceAfter = 1,
            Reason = string.IsNullOrWhiteSpace(request.Reason)
                ? $"Pase individual para la clase de {session.Activity?.Name ?? "actividad"} ({_gymCalendar.ToLocal(session.StartsAtUtc):yyyy-MM-dd})"
                : request.Reason.Trim(),
            PerformedByUserId = performedByUserId,
            CreatedAtUtc = _gymCalendar.UtcNow,
            IsActive = true
        };

        _dbContext.MembershipCreditMovements.Add(movement);
        await _dbContext.SaveChangesAsync(cancellationToken);

        movement.Student = student;
        movement.ClassSession = session;

        return MapToDto(movement);
    }

    private static CreditMovementDto MapToDto(MembershipCreditMovement cm) =>
        new(
            cm.Id,
            cm.MembershipId,
            cm.StudentId,
            cm.Student?.FullName ?? string.Empty,
            cm.ClassSessionId,
            cm.Type,
            cm.Amount,
            cm.BalanceAfter,
            cm.Reason,
            cm.PerformedByUserId,
            cm.CreatedAtUtc,
            cm.ReservationId,
            cm.ExpiresOn,
            cm.ActivityId
        );
}
