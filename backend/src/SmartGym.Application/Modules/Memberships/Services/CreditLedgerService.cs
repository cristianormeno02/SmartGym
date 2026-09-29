using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Memberships.Services;

public class CreditLedgerService : ICreditLedgerService
{
    private readonly ISmartGymDbContext _dbContext;

    public CreditLedgerService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeMembership = await _dbContext.Memberships
            .Include(m => m.MembershipPlan)
            .Where(m => m.StudentId == request.StudentId && m.IsActive)
            .Where(m => m.Status == MembershipStatus.Active || m.Status == MembershipStatus.Depleted)
            .OrderByDescending(m => m.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        MembershipCreditMovement movement;

        if (activeMembership != null)
        {
            movement = activeMembership.AddCompensatoryCredit(request.ClassSessionId, request.Reason, performedByUserId);
        }
        else
        {
            movement = new MembershipCreditMovement
            {
                Id = Guid.NewGuid(),
                StudentId = request.StudentId,
                ClassSessionId = request.ClassSessionId,
                Type = CreditMovementType.ClassSuspensionCompensation,
                Amount = 1,
                BalanceAfter = 1,
                Reason = request.Reason,
                PerformedByUserId = performedByUserId,
                CreatedAtUtc = DateTime.UtcNow,
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
                ? $"Pase individual para la clase de {session.Activity?.Name ?? "actividad"} ({session.Date:yyyy-MM-dd})"
                : request.Reason.Trim(),
            PerformedByUserId = performedByUserId,
            CreatedAtUtc = DateTime.UtcNow,
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
            cm.CreatedAtUtc
        );
}
