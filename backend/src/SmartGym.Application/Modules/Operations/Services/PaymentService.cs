using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public class PaymentService : IPaymentService
{
    private readonly ISmartGymDbContext _dbContext;

    public PaymentService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<PaymentDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.Payments
            .Include(p => p.PayerPerson)
            .AsNoTracking()
            .OrderByDescending(p => p.PaidAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<PaymentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await _dbContext.Payments
            .Include(p => p.PayerPerson)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return payment == null ? null : MapToDto(payment);
    }

    public async Task<List<PaymentDto>> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.Payments
            .Include(p => p.PayerPerson)
            .AsNoTracking()
            .Where(p => p.PayerPersonId == personId)
            .OrderByDescending(p => p.PaidAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<PaymentDto> RegisterPaymentAsync(RegisterPaymentRequest request, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var payer = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == request.PayerPersonId && p.IsActive, cancellationToken);
        if (payer == null)
        {
            throw new ArgumentException("La persona que realiza el pago no existe o no está activa.");
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentException("El monto del pago debe ser superior a cero.");
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = request.PayerPersonId,
            MembershipId = request.MembershipId,
            ClassSessionId = request.ClassSessionId,
            Amount = request.Amount,
            Method = request.Method,
            Status = PaymentStatus.Completed,
            ExternalTransactionId = request.ExternalTransactionId?.Trim(),
            Notes = request.Notes?.Trim(),
            RegisteredByUserId = currentUserId,
            PaidAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.Payments.Add(payment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        payment.PayerPerson = payer;
        return MapToDto(payment);
    }

    public async Task<PaymentDto?> RefundPaymentAsync(Guid id, string reason, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var payment = await _dbContext.Payments
            .Include(p => p.PayerPerson)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment == null) return null;

        payment.Refund(reason);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(payment);
    }

    private static PaymentDto MapToDto(Payment p) =>
        new(
            p.Id,
            p.PayerPersonId,
            p.PayerPerson?.FullName ?? string.Empty,
            p.MembershipId,
            p.ClassSessionId,
            p.Amount,
            p.Method,
            p.Status,
            p.ExternalTransactionId,
            p.ReceiptUrl,
            p.Notes,
            p.PaidAtUtc
        );
}
