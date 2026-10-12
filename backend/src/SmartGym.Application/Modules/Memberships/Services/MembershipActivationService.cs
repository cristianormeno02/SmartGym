using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Memberships.Services;

public class MembershipActivationService : IMembershipActivationService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly ICreditLedgerService _creditLedgerService;
    private readonly IGymCalendar _gymCalendar;
    private readonly IAuditService? _auditService;
    private readonly INotificationDispatcher _notificationDispatcher;

    public MembershipActivationService(
        ISmartGymDbContext dbContext,
        ICreditLedgerService creditLedgerService,
        IGymCalendar? gymCalendar = null,
        IAuditService? auditService = null,
        INotificationDispatcher? notificationDispatcher = null)
    {
        _dbContext = dbContext;
        _creditLedgerService = creditLedgerService;
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(new GymOptions()));
        _auditService = auditService;
        _notificationDispatcher = notificationDispatcher ?? new NotificationDispatcher(
            dbContext,
            new NotificationRecipientResolver(dbContext, _gymCalendar),
            new NotificationTemplateRenderer(dbContext),
            new NotificationService(dbContext, _gymCalendar),
            _gymCalendar);
    }

    public async Task<bool> TryActivateAsync(
        Guid membershipId,
        Guid? authorizedByUserId = null,
        string? activationReason = null,
        bool isExplicitActivation = false,
        bool isCallerAdministrator = false,
        bool saveChanges = true,
        CancellationToken cancellationToken = default)
    {
        var membership = await _dbContext.Memberships
            .Include(m => m.StatusChanges)
            .Include(m => m.Suspensions)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.IsActive, cancellationToken)
            ?? throw new NotFoundException("La membresía especificada no existe.");

        // Idempotent if already Active
        if (membership.Status == MembershipStatus.Active)
        {
            await _creditLedgerService.RecordInitialCreditsAsync(membership, authorizedByUserId, cancellationToken);
            if (saveChanges)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return true;
        }

        if (membership.Status != MembershipStatus.Pending)
        {
            if (isExplicitActivation)
            {
                throw new ConflictException($"Sólo se puede activar una membresía en estado Pending (estado actual: {membership.Status}).");
            }

            return false;
        }

        var totalPaidAmount = await CalculateTotalCompletedPaymentsAsync(membership.Id, cancellationToken);
        var isFullyCovered = membership.PurchasePrice == 0m || totalPaidAmount >= membership.PurchasePrice;

        string resolvedReason;

        if (isFullyCovered)
        {
            resolvedReason = string.IsNullOrWhiteSpace(activationReason)
                ? "Membresía activada por cobertura de pago"
                : activationReason.Trim();
        }
        else
        {
            if (!isExplicitActivation)
            {
                return false;
            }

            if (!isCallerAdministrator)
            {
                throw new ForbiddenException("Sólo un Administrador puede activar una membresía con saldo pendiente.");
            }

            if (string.IsNullOrWhiteSpace(activationReason) || activationReason.Trim().Length > 500)
            {
                throw new ValidationException("El motivo de activación (entre 1 y 500 caracteres) es obligatorio para activar una membresía con saldo pendiente.");
            }

            resolvedReason = activationReason.Trim();
        }

        var today = _gymCalendar.Today;
        var nowUtc = _gymCalendar.UtcNow;

        membership.Activate(today, nowUtc, authorizedByUserId, resolvedReason);
        await _creditLedgerService.RecordInitialCreditsAsync(membership, authorizedByUserId, cancellationToken);

        try
        {
            await _notificationDispatcher.DispatchAsync(
                new DispatchNotificationCommand(
                    TargetPersonId: membership.StudentId,
                    Type: NotificationType.MembershipActivated,
                    Variables: new Dictionary<string, string?>
                    {
                        ["planName"] = "Membresía",
                        ["startDate"] = $"{membership.StartDate:yyyy-MM-dd}",
                        ["endDate"] = $"{membership.EndDate:yyyy-MM-dd}"
                    },
                    IdempotencyKey: $"membership-activated:{membership.Id}",
                    RelatedEntityType: "Membership",
                    RelatedEntityId: membership.Id,
                    ActionUrl: "/portal/alumno",
                    DirectTitleOverride: "Membresía activada",
                    DirectBodyOverride: $"Tu membresía ha sido activada con vigencia del {membership.StartDate:yyyy-MM-dd} al {membership.EndDate:yyyy-MM-dd}.",
                    SaveChanges: false),
                cancellationToken);
        }
        catch (Exception)
        {
            // El fallo de notificación no revierte la activación de la membresía
        }

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "MEMBERSHIP_ACTIVATED",
                "Membership",
                membership.Id.ToString(),
                JsonSerializer.Serialize(new { Status = MembershipStatus.Pending.ToString() }),
                JsonSerializer.Serialize(new
                {
                    Status = MembershipStatus.Active.ToString(),
                    Reason = resolvedReason,
                    TotalPaidAmount = totalPaidAmount,
                    membership.PurchasePrice,
                    membership.AvailableCredits
                }),
                authorizedByUserId,
                null,
                null,
                cancellationToken);
        }

        if (saveChanges)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private async Task<decimal> CalculateTotalCompletedPaymentsAsync(Guid membershipId, CancellationToken cancellationToken)
    {
        var persistedPayments = await _dbContext.Payments
            .Where(p => p.MembershipId == membershipId && p.IsActive)
            .ToListAsync(cancellationToken);

        var allPayments = new Dictionary<Guid, Payment>();
        foreach (var p in persistedPayments)
        {
            allPayments[p.Id] = p;
        }

        if (_dbContext is DbContext efContext)
        {
            foreach (var entry in efContext.ChangeTracker.Entries<Payment>())
            {
                if (entry.State != EntityState.Deleted &&
                    entry.Entity.IsActive &&
                    entry.Entity.MembershipId == membershipId)
                {
                    allPayments[entry.Entity.Id] = entry.Entity;
                }
            }
        }

        // Importe neto: un pago parcialmente devuelto cuenta por lo que no se devolvió
        return allPayments.Values
            .Where(p => p.IsActive && p.Status.HasNetAmount())
            .Sum(p => p.NetAmount);
    }
}
