using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Application.Modules.Operations.Validators;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using ValidationException = SmartGym.Application.Common.Exceptions.ValidationException;

namespace SmartGym.Application.Modules.Operations.Services;

public partial class PaymentService : IPaymentService
{
    private const int MaxPageSize = 100;
    private const int MaxIdempotencyKeyLength = 100;

    private static readonly RegisterPaymentRequestValidator RegisterValidator = new();
    private static readonly ApplyPaymentRequestValidator ApplyValidator = new();
    private static readonly FailPaymentRequestValidator FailValidator = new();
    private static readonly CancelPaymentRequestValidator CancelValidator = new();
    private static readonly CreatePaymentRefundRequestValidator RefundValidator = new();

    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly GymOptions _gymOptions;
    private readonly ICurrentUserService? _currentUserService;
    private readonly IPaymentProviderResolver? _providerResolver;
    private readonly PaymentsOptions _paymentsOptions;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        ISmartGymDbContext dbContext,
        IGymCalendar? gymCalendar = null,
        IOptions<GymOptions>? gymOptions = null,
        ICurrentUserService? currentUserService = null,
        IPaymentProviderResolver? providerResolver = null,
        IOptions<PaymentsOptions>? paymentsOptions = null,
        ILogger<PaymentService>? logger = null)
    {
        _providerResolver = providerResolver;
        _paymentsOptions = paymentsOptions?.Value ?? new PaymentsOptions();
        _logger = logger ?? NullLogger<PaymentService>.Instance;
        _dbContext = dbContext;
        _gymOptions = gymOptions?.Value ?? new GymOptions();
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(_gymOptions));
        _currentUserService = currentUserService;
    }

    // ---- Consultas ----

    public async Task<PagedResult<PaymentDto>> GetPagedAsync(PaymentListFilter filter, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var query = _dbContext.Payments.AsNoTracking().Where(p => p.IsActive);

        if (filter.StudentId.HasValue)
        {
            query = query.Where(p => p.PayerPersonId == filter.StudentId.Value);
        }

        if (filter.MembershipId.HasValue)
        {
            query = query.Where(p => p.MembershipId == filter.MembershipId.Value);
        }

        if (filter.ClassSessionId.HasValue)
        {
            query = query.Where(p => p.ClassSessionId == filter.ClassSessionId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(p => p.Status == filter.Status.Value);
        }

        if (filter.Method.HasValue)
        {
            query = query.Where(p => p.Method == filter.Method.Value);
        }

        if (filter.Provider.HasValue)
        {
            query = query.Where(p => p.Provider == filter.Provider.Value);
        }

        if (filter.FromUtc.HasValue)
        {
            query = query.Where(p => p.PaidAtUtc >= filter.FromUtc.Value);
        }

        if (filter.ToUtc.HasValue)
        {
            query = query.Where(p => p.PaidAtUtc < filter.ToUtc.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(p => p.PayerPerson)
            .OrderByDescending(p => p.PaidAtUtc)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PaymentDto>(items.Select(MapToDto).ToList(), totalCount, page, pageSize);
    }

    public async Task<PaymentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await _dbContext.Payments
            .Include(p => p.PayerPerson)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive, cancellationToken);

        return payment == null ? null : MapToDto(payment);
    }

    public async Task<List<PaymentDto>> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.Payments
            .Include(p => p.PayerPerson)
            .AsNoTracking()
            .Where(p => p.PayerPersonId == personId && p.IsActive)
            .OrderByDescending(p => p.PaidAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<List<PaymentRefundDto>> GetRefundsAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Payments.AnyAsync(p => p.Id == paymentId && p.IsActive, cancellationToken);
        if (!exists)
        {
            throw PaymentNotFound();
        }

        var refunds = await _dbContext.PaymentRefunds
            .AsNoTracking()
            .Where(r => r.PaymentId == paymentId && r.IsActive)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return refunds.Select(MapToDto).ToList();
    }

    public async Task<MembershipBalanceDto> GetMembershipBalanceAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        var membership = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.Id == membershipId && m.IsActive)
            .Select(m => new { m.Id, m.StudentId, m.PurchasePrice, m.Currency })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.WithCode(PaymentErrorCodes.ObligationNotFound, "La membresía especificada no existe.");

        var payments = await _dbContext.Payments
            .Include(p => p.PayerPerson)
            .AsNoTracking()
            .Where(p => p.MembershipId == membershipId && p.IsActive)
            .OrderBy(p => p.PaidAtUtc)
            .ToListAsync(cancellationToken);

        var paymentIds = payments.Select(p => p.Id).ToList();
        var refunds = await _dbContext.PaymentRefunds
            .AsNoTracking()
            .Where(r => paymentIds.Contains(r.PaymentId) && r.IsActive)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var totalPaid = payments.Where(p => p.Status.HasNetAmount()).Sum(p => p.NetAmount);
        var pendingPayments = payments.Where(p => p.Status == PaymentStatus.Pending).Sum(p => p.Amount);

        return new MembershipBalanceDto(
            membership.Id,
            membership.StudentId,
            membership.PurchasePrice,
            membership.Currency,
            totalPaid,
            pendingPayments,
            Math.Max(0m, membership.PurchasePrice - totalPaid),
            payments.Select(MapToDto).ToList(),
            refunds.Select(MapToDto).ToList());
    }

    // ---- Registro ----

    public async Task<PaymentDto> RegisterPaymentAsync(RegisterPaymentRequest request, Guid currentUserId, CancellationToken cancellationToken = default) =>
        (await RegisterPaymentAsync(request, currentUserId, idempotencyKey: null, cancellationToken)).Value;

    public async Task<IdempotentResult<PaymentDto>> RegisterPaymentAsync(
        RegisterPaymentRequest request,
        Guid currentUserId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Validate(RegisterValidator, request);
        var key = NormalizeIdempotencyKey(idempotencyKey);

        var payer = await _dbContext.People
            .FirstOrDefaultAsync(p => p.Id == request.PayerPersonId && p.IsActive, cancellationToken)
            ?? throw new ValidationException("La persona que realiza el pago no existe o no está activa.");

        var requestedCurrency = request.Currency?.ToUpperInvariant();

        await using var transaction = await BeginOwnTransactionAsync(cancellationToken);

        if (key != null)
        {
            // El bloqueo del pagador serializa los reintentos concurrentes con la misma clave
            await _dbContext.LockPersonForUpdateAsync(payer.Id, cancellationToken);

            var existing = await _dbContext.Payments
                .Include(p => p.PayerPerson)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PayerPersonId == payer.Id && p.IdempotencyKey == key, cancellationToken);

            if (existing != null)
            {
                var sameRequest =
                    existing.Amount == request.Amount &&
                    existing.Method == request.Method &&
                    existing.MembershipId == request.MembershipId &&
                    existing.ClassSessionId == request.ClassSessionId &&
                    (requestedCurrency == null || existing.Currency == requestedCurrency);

                if (!sameRequest)
                {
                    throw ConflictException.WithCode(
                        PaymentErrorCodes.IdempotencyKeyMismatch,
                        "La clave de idempotencia ya fue utilizada con otros datos de pago.");
                }

                await CommitAsync(transaction, cancellationToken);
                return new IdempotentResult<PaymentDto>(MapToDto(existing), Created: false);
            }
        }

        string currency;
        if (request.MembershipId.HasValue)
        {
            await _dbContext.LockMembershipForUpdateAsync(request.MembershipId.Value, cancellationToken);
            currency = await ValidateMembershipObligationAsync(
                request.MembershipId.Value, payer.Id, requestedCurrency, request.Amount, cancellationToken);
        }
        else
        {
            if (request.ClassSessionId.HasValue)
            {
                await ValidateClassSessionObligationAsync(request.ClassSessionId.Value, cancellationToken);
            }

            currency = requestedCurrency ?? DefaultCurrency;
        }

        var now = _gymCalendar.UtcNow;
        var userId = NullIfEmpty(currentUserId);
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = payer.Id,
            MembershipId = request.MembershipId,
            ClassSessionId = request.ClassSessionId,
            Amount = request.Amount,
            Currency = currency,
            Method = request.Method,
            Provider = PaymentProvider.Manual,
            Status = PaymentStatus.Pending,
            ExternalTransactionId = string.IsNullOrWhiteSpace(request.ExternalTransactionId) ? null : request.ExternalTransactionId.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            RegisteredByUserId = userId,
            IdempotencyKey = key,
            PaidAtUtc = now,
            CreatedAtUtc = now,
            IsActive = true
        };

        if (request.Status == PaymentStatus.Completed)
        {
            payment.Complete(nowUtc: now);
        }

        _dbContext.Payments.Add(payment);
        AddAuditLog("PAYMENT_REGISTERED", payment.Id, userId, oldValues: null, newValues: Snapshot(payment));

        await SaveChangesMappingConcurrencyAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        payment.PayerPerson = payer;
        return new IdempotentResult<PaymentDto>(MapToDto(payment), Created: true);
    }

    // ---- Transiciones ----

    public async Task<PaymentDto> ConfirmPaymentAsync(
        Guid id,
        ConfirmPaymentRequest? request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginOwnTransactionAsync(cancellationToken);
        var payment = await LockAndLoadAsync(id, targetMembershipId: null, cancellationToken);

        EnsureManual(payment);
        EnsureStatus(payment, PaymentStatus.Pending, "confirmar");

        // Un pago pendiente no reserva saldo: otro cobro pudo cubrir la membresía entretanto
        if (payment.MembershipId.HasValue)
        {
            await EnsureFitsMembershipBalanceAsync(payment.MembershipId.Value, payment.Amount, cancellationToken);
        }

        var before = Snapshot(payment);

        if (!string.IsNullOrWhiteSpace(request?.Notes))
        {
            payment.Notes = string.IsNullOrWhiteSpace(payment.Notes)
                ? request.Notes.Trim()
                : $"{payment.Notes} | {request.Notes.Trim()}";
        }

        payment.Complete(request?.ExternalTransactionId, _gymCalendar.UtcNow);
        AddAuditLog("PAYMENT_CONFIRMED", payment.Id, NullIfEmpty(currentUserId), before, Snapshot(payment));

        await SaveChangesMappingConcurrencyAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return MapToDto(payment);
    }

    public async Task<PaymentDto> FailPaymentAsync(
        Guid id,
        FailPaymentRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        Validate(FailValidator, request ?? new FailPaymentRequest(string.Empty));

        await using var transaction = await BeginOwnTransactionAsync(cancellationToken);
        var payment = await LockAndLoadAsync(id, targetMembershipId: null, cancellationToken);

        EnsureManual(payment);
        EnsureStatus(payment, PaymentStatus.Pending, "rechazar");

        var before = Snapshot(payment);
        payment.Fail(request!.Reason, _gymCalendar.UtcNow);
        AddAuditLog("PAYMENT_FAILED", payment.Id, NullIfEmpty(currentUserId), before, Snapshot(payment, request.Reason));

        await SaveChangesMappingConcurrencyAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return MapToDto(payment);
    }

    public async Task<PaymentDto> CancelPaymentAsync(
        Guid id,
        CancelPaymentRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        Validate(CancelValidator, request ?? new CancelPaymentRequest(string.Empty));

        await using var transaction = await BeginOwnTransactionAsync(cancellationToken);
        var payment = await LockAndLoadAsync(id, targetMembershipId: null, cancellationToken);

        EnsureStatus(payment, PaymentStatus.Pending, "cancelar");

        var before = Snapshot(payment);
        payment.Cancel(request!.Reason, _gymCalendar.UtcNow);
        AddAuditLog("PAYMENT_CANCELLED", payment.Id, NullIfEmpty(currentUserId), before, Snapshot(payment, request.Reason));

        await SaveChangesMappingConcurrencyAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return MapToDto(payment);
    }

    public async Task<PaymentDto> ApplyPaymentAsync(
        Guid id,
        ApplyPaymentRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        Validate(ApplyValidator, request);

        await using var transaction = await BeginOwnTransactionAsync(cancellationToken);
        var payment = await LockAndLoadAsync(id, request.MembershipId, cancellationToken);

        if (payment.IsApplied)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.AlreadyApplied,
                "El pago ya se encuentra aplicado a un concepto.");
        }

        if (payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Completed)
        {
            throw InvalidTransition($"No se puede aplicar un pago en estado {payment.Status}.");
        }

        var before = Snapshot(payment);
        var userId = NullIfEmpty(currentUserId);
        var now = _gymCalendar.UtcNow;

        if (request.MembershipId.HasValue)
        {
            await ValidateMembershipObligationAsync(
                request.MembershipId.Value, payment.PayerPersonId, payment.Currency, payment.Amount, cancellationToken);
            payment.ApplyToMembership(request.MembershipId.Value, userId, now);
        }
        else
        {
            await ValidateClassSessionObligationAsync(request.ClassSessionId!.Value, cancellationToken);
            payment.ApplyToClassSession(request.ClassSessionId.Value, now);
        }

        AddAuditLog("PAYMENT_APPLIED", payment.Id, userId, before, Snapshot(payment));

        await SaveChangesMappingConcurrencyAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return MapToDto(payment);
    }

    // ---- Devoluciones ----

    public async Task<IdempotentResult<PaymentRefundDto>> CreateRefundAsync(
        Guid paymentId,
        CreatePaymentRefundRequest request,
        Guid currentUserId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Validate(RefundValidator, request);
        var key = NormalizeIdempotencyKey(idempotencyKey);
        var reason = request.Reason.Trim();

        await using var transaction = await BeginOwnTransactionAsync(cancellationToken);
        var payment = await LockAndLoadAsync(paymentId, targetMembershipId: null, cancellationToken);

        // La devolución de un pago externo se ejecuta contra el proveedor, en varios pasos
        if (payment.IsExternal)
        {
            return await CreateExternalRefundAsync(payment, transaction, request, reason, key, currentUserId, cancellationToken);
        }

        if (key != null)
        {
            var existing = await _dbContext.PaymentRefunds
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.PaymentId == paymentId && r.IdempotencyKey == key, cancellationToken);

            if (existing != null)
            {
                if (existing.Amount != request.Amount || existing.Reason != reason)
                {
                    throw ConflictException.WithCode(
                        PaymentErrorCodes.IdempotencyKeyMismatch,
                        "La clave de idempotencia ya fue utilizada con otros datos de devolución.");
                }

                await CommitAsync(transaction, cancellationToken);
                return new IdempotentResult<PaymentRefundDto>(MapToDto(existing), Created: false);
            }
        }

        if (payment.Status != PaymentStatus.Completed && payment.Status != PaymentStatus.PartiallyRefunded)
        {
            throw InvalidTransition($"Sólo se puede devolver un pago confirmado (estado actual: {payment.Status}).");
        }

        if (request.Amount > payment.NetAmount)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.RefundAmountExceeded,
                $"La devolución supera el importe disponible para devolver ({payment.NetAmount:0.00} {payment.Currency}).",
                new { payment.Amount, payment.RefundedAmount, Available = payment.NetAmount });
        }

        var before = Snapshot(payment);
        var userId = NullIfEmpty(currentUserId);
        var refund = payment.RegisterRefund(request.Amount, reason, userId, key, request.Notes, _gymCalendar.UtcNow);

        // Alta explícita: una entidad con clave asignada alcanzada sólo por navegación se asumiría existente
        _dbContext.PaymentRefunds.Add(refund);
        AddAuditLog(
            "PAYMENT_REFUNDED",
            payment.Id,
            userId,
            before,
            new { payment.RefundedAmount, Status = payment.Status.ToString(), RefundId = refund.Id, RefundAmount = refund.Amount, refund.Currency, Reason = reason });

        await SaveChangesMappingConcurrencyAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new IdempotentResult<PaymentRefundDto>(MapToDto(refund), Created: true);
    }

    // ---- Obligaciones ----

    /// <summary>
    /// Valida que la membresía pueda recibir el pago y devuelve su moneda. Debe invocarse con la membresía
    /// bloqueada, para que el saldo no cambie entre la validación y el guardado.
    /// </summary>
    private async Task<string> ValidateMembershipObligationAsync(
        Guid membershipId,
        Guid payerPersonId,
        string? paymentCurrency,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var membership = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.Id == membershipId && m.IsActive)
            .Select(m => new { m.StudentId, m.Status, m.Currency })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.WithCode(PaymentErrorCodes.ObligationNotFound, "La membresía especificada no existe.");

        if (membership.Status == MembershipStatus.Cancelled)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.ObligationCancelled,
                "No se puede aplicar un pago a una membresía cancelada.");
        }

        if (membership.StudentId != payerPersonId)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.ObligationStudentMismatch,
                "La membresía pertenece a un alumno distinto del pagador.");
        }

        if (paymentCurrency != null && !string.Equals(paymentCurrency, membership.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.CurrencyMismatch,
                $"La moneda del pago ({paymentCurrency}) no coincide con la de la membresía ({membership.Currency}).");
        }

        await EnsureFitsMembershipBalanceAsync(membershipId, amount, cancellationToken);
        return membership.Currency;
    }

    private async Task EnsureFitsMembershipBalanceAsync(Guid membershipId, decimal amount, CancellationToken cancellationToken)
    {
        var purchasePrice = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.Id == membershipId)
            .Select(m => m.PurchasePrice)
            .FirstAsync(cancellationToken);

        // Sólo cuenta el neto de los cobros confirmados: los pendientes no reservan saldo
        var netPaid = await _dbContext.Payments
            .Where(p => p.MembershipId == membershipId && p.IsActive &&
                        (p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.PartiallyRefunded))
            .SumAsync(p => (decimal?)(p.Amount - p.RefundedAmount), cancellationToken) ?? 0m;

        var pendingBalance = Math.Max(0m, purchasePrice - netPaid);
        if (amount > pendingBalance)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.ObligationOverpaid,
                $"El importe del pago supera el saldo pendiente de la membresía ({pendingBalance:0.00}).",
                new { PurchasePrice = purchasePrice, TotalPaidAmount = netPaid, PendingBalance = pendingBalance });
        }
    }

    private async Task ValidateClassSessionObligationAsync(Guid classSessionId, CancellationToken cancellationToken)
    {
        var session = await _dbContext.ClassSessions
            .AsNoTracking()
            .Where(cs => cs.Id == classSessionId && cs.IsActive)
            .Select(cs => new { cs.Status })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.WithCode(PaymentErrorCodes.ObligationNotFound, "La clase especificada no existe.");

        if (session.Status == ClassSessionStatus.Cancelled)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.ObligationCancelled,
                "No se puede aplicar un pago a una clase cancelada.");
        }
    }

    // ---- Concurrencia ----

    /// <summary>
    /// Los bloqueos de fila requieren una transacción. Si quien llama ya abrió una (la contratación de una
    /// membresía), se usa esa y es ella quien confirma; si no, la operación abre y confirma la propia.
    /// </summary>
    private async Task<IDbContextTransaction?> BeginOwnTransactionAsync(CancellationToken cancellationToken) =>
        _dbContext.Database.CurrentTransaction == null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static async Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Bloquea la membresía afectada (la del pago o la de destino) y luego el pago, y recién entonces lee su
    /// estado: así dos operaciones concurrentes sobre el mismo pago o la misma membresía se ven entre sí.
    /// </summary>
    private async Task<Payment> LockAndLoadAsync(Guid id, Guid? targetMembershipId, CancellationToken cancellationToken)
    {
        var peek = await _dbContext.Payments
            .AsNoTracking()
            .Where(p => p.Id == id && p.IsActive)
            .Select(p => new { p.MembershipId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw PaymentNotFound();

        var membershipToLock = peek.MembershipId ?? targetMembershipId;
        if (membershipToLock.HasValue)
        {
            await _dbContext.LockMembershipForUpdateAsync(membershipToLock.Value, cancellationToken);
        }

        await _dbContext.LockPaymentForUpdateAsync(id, cancellationToken);

        var payment = await _dbContext.Payments
            .Include(p => p.PayerPerson)
            .FirstAsync(p => p.Id == id, cancellationToken);

        // Si ya estaba en el contexto, sus valores pueden ser anteriores al bloqueo
        var entry = _dbContext.Payments.Entry(payment);
        if (entry.State == EntityState.Unchanged)
        {
            await entry.ReloadAsync(cancellationToken);
        }

        // El pago se aplicó a otra membresía mientras se esperaba el bloqueo: se tomó el bloqueo equivocado
        if (payment.MembershipId != peek.MembershipId)
        {
            throw ConcurrencyConflict();
        }

        return payment;
    }

    private async Task SaveChangesMappingConcurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrencyConflict();
        }
    }

    // ---- Auxiliares ----

    private string DefaultCurrency =>
        string.IsNullOrWhiteSpace(_gymOptions.DefaultCurrency) ? "ARS" : _gymOptions.DefaultCurrency.ToUpperInvariant();

    private static Guid? NullIfEmpty(Guid id) => id == Guid.Empty ? null : id;

    private static string? NormalizeIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();
        if (trimmed.Length > MaxIdempotencyKeyLength)
        {
            throw new ValidationException($"La clave de idempotencia no puede superar los {MaxIdempotencyKeyLength} caracteres.");
        }

        return trimmed;
    }

    private static void Validate<T>(IValidator<T> validator, T request)
    {
        if (request == null)
        {
            throw new ValidationException("La solicitud es obligatoria.");
        }

        var result = validator.Validate(request);
        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }
    }

    private static void EnsureStatus(Payment payment, PaymentStatus expected, string verb)
    {
        if (payment.Status != expected)
        {
            throw InvalidTransition($"Sólo se puede {verb} un pago en estado {expected} (estado actual: {payment.Status}).");
        }
    }

    // Un pago de proveedor externo sólo se confirma o rechaza con información verificada del proveedor.
    private static void EnsureManual(Payment payment)
    {
        if (payment.IsExternal)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.ExternalPaymentManualConfirmForbidden,
                "Un pago de proveedor externo no se puede confirmar ni rechazar manualmente.");
        }
    }

    private static NotFoundException PaymentNotFound() =>
        NotFoundException.WithCode(PaymentErrorCodes.NotFound, "Pago no encontrado.");

    private static ConflictException InvalidTransition(string message) =>
        ConflictException.WithCode(PaymentErrorCodes.InvalidTransition, message);

    private static ConflictException ConcurrencyConflict() =>
        ConflictException.WithCode(
            PaymentErrorCodes.ConcurrencyConflict,
            "El pago fue modificado por otra operación. Volvé a consultarlo e intentá nuevamente.");

    /// <summary>
    /// La auditoría se agrega al mismo guardado que la operación (no usa <c>IAuditService</c>, que guarda por su
    /// cuenta): así queda en la misma transacción y no adelanta el guardado de quien llama.
    /// </summary>
    private void AddAuditLog(
        string action,
        Guid paymentId,
        Guid? userId,
        object? oldValues,
        object? newValues,
        string entityName = nameof(Payment),
        bool systemAction = false)
    {
        var nowUtc = _gymCalendar.UtcNow;
        // Una acción del sistema (webhook, vencimiento) no se atribuye al usuario de la solicitud en curso
        var isAuthenticated = !systemAction && _currentUserService is { IsAuthenticated: true };

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = action,
            EntityName = entityName,
            EntityId = paymentId.ToString(),
            OldValues = oldValues == null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = newValues == null ? null : JsonSerializer.Serialize(newValues),
            UserId = userId ?? (isAuthenticated ? _currentUserService!.UserId : null),
            UserEmail = isAuthenticated ? _currentUserService!.Email : null,
            TimestampUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            IsActive = true
        });
    }

    private static object Snapshot(Payment p, string? reason = null) => new
    {
        p.PayerPersonId,
        p.MembershipId,
        p.ClassSessionId,
        p.Amount,
        p.RefundedAmount,
        p.Currency,
        Method = p.Method.ToString(),
        Status = p.Status.ToString(),
        Reason = reason
    };

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
            p.PaidAtUtc,
            p.Provider,
            p.Currency,
            p.RefundedAmount,
            p.NetAmount,
            p.ConfirmedAtUtc,
            p.CancelledAtUtc,
            p.CheckoutUrl,
            p.ExpiresAtUtc
        );

    private static PaymentRefundDto MapToDto(PaymentRefund r) =>
        new(
            r.Id, r.PaymentId, r.Amount, r.Currency, r.Reason, r.Notes, r.RequestedByUserId, r.CreatedAtUtc,
            r.Status, r.ExternalRefundId, r.CompletedAtUtc, r.FailureReason);
}
