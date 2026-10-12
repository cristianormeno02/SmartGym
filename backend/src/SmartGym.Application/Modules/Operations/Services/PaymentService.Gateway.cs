using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using ValidationException = SmartGym.Application.Common.Exceptions.ValidationException;

namespace SmartGym.Application.Modules.Operations.Services;

/// <summary>
/// Pasarela externa: checkout, vencimiento, webhooks, devoluciones contra el proveedor, conciliación y
/// discrepancias. Ninguna llamada al proveedor ocurre con una transacción abierta ni con filas bloqueadas:
/// cada flujo guarda su estado, llama al proveedor y vuelve a bloquear para registrar el resultado.
/// </summary>
public partial class PaymentService
{
    private const string ExpiredCheckoutReason = "Checkout vencido sin pago";

    // Largo de las columnas que guardan identificadores del proveedor
    private const int MaxExternalIdLength = 150;

    // ---- Checkout ----

    public async Task<IdempotentResult<PaymentDto>> CreateCheckoutAsync(
        CreatePaymentCheckoutRequest request,
        Guid currentUserId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request == null || request.MembershipId == Guid.Empty)
        {
            throw new ValidationException("La membresía a pagar es obligatoria.");
        }

        var key = NormalizeIdempotencyKey(idempotencyKey);
        var provider = ResolveProvider(request.Provider);
        var userId = NullIfEmpty(currentUserId);

        var membership = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.Id == request.MembershipId && m.IsActive)
            .Select(m => new { m.StudentId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.WithCode(PaymentErrorCodes.ObligationNotFound, "La membresía especificada no existe.");

        // 1. Se guarda el pago pendiente: su Id es la referencia que el proveedor devolverá en la notificación.
        Payment payment;
        var created = false;
        await using (var transaction = await BeginOwnTransactionAsync(cancellationToken))
        {
            await _dbContext.LockPersonForUpdateAsync(membership.StudentId, cancellationToken);

            Payment? existing = null;
            if (key != null)
            {
                existing = await _dbContext.Payments
                    .Include(p => p.PayerPerson)
                    .FirstOrDefaultAsync(p => p.PayerPersonId == membership.StudentId && p.IdempotencyKey == key, cancellationToken);

                if (existing != null && (existing.MembershipId != request.MembershipId || existing.Provider != request.Provider))
                {
                    throw ConflictException.WithCode(
                        PaymentErrorCodes.IdempotencyKeyMismatch,
                        "La clave de idempotencia ya fue utilizada con otros datos de pago.");
                }
            }

            if (existing != null)
            {
                // Sólo un pago pendiente al que le falta la URL (corte tras crearlo) necesita volver al proveedor
                if (existing.Status != PaymentStatus.Pending || !string.IsNullOrWhiteSpace(existing.CheckoutUrl))
                {
                    await CommitAsync(transaction, cancellationToken);
                    return new IdempotentResult<PaymentDto>(MapToDto(existing), Created: false);
                }

                payment = existing;
            }
            else
            {
                await _dbContext.LockMembershipForUpdateAsync(request.MembershipId, cancellationToken);
                payment = await CreatePendingCheckoutPaymentAsync(request, membership.StudentId, key, userId, cancellationToken);
                created = true;
                await SaveChangesMappingConcurrencyAsync(cancellationToken);
            }

            await CommitAsync(transaction, cancellationToken);
        }

        // 2. Llamada al proveedor, sin transacción ni bloqueos.
        PaymentCheckoutResult? checkout = null;
        Exception? failure = null;
        try
        {
            checkout = await provider.CreateCheckoutAsync(
                new PaymentCheckoutContext(
                    payment.Id,
                    payment.Amount,
                    payment.Currency,
                    "Membresía SmartGym",
                    payment.ExpiresAtUtc ?? _gymCalendar.UtcNow.AddMinutes(_paymentsOptions.CheckoutExpirationMinutes)),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex;
            _logger.LogError(ex, "El proveedor {Provider} no pudo crear el checkout del pago {PaymentId}.", request.Provider, payment.Id);
        }

        // 3. Se registra el resultado.
        await using (var transaction = await BeginOwnTransactionAsync(cancellationToken))
        {
            payment = await LockAndLoadAsync(payment.Id, targetMembershipId: null, cancellationToken);

            if (payment.Status == PaymentStatus.Pending)
            {
                if (checkout != null)
                {
                    payment.ExternalReference = checkout.ExternalReference;
                    payment.CheckoutUrl = checkout.CheckoutUrl;
                    payment.UpdatedAtUtc = _gymCalendar.UtcNow;
                }
                else
                {
                    // No es un rechazo del cobro: no se avisa al pagador
                    payment.Fail("El proveedor de pagos no respondió al crear el checkout", _gymCalendar.UtcNow, notifyPayer: false);
                    AddAuditLog("PAYMENT_FAILED", payment.Id, userId, oldValues: null, Snapshot(payment, "Proveedor no disponible"));
                }

                await SaveChangesMappingConcurrencyAsync(cancellationToken);
            }

            await CommitAsync(transaction, cancellationToken);
        }

        if (checkout == null)
        {
            throw new ExternalServiceException(
                PaymentErrorCodes.ProviderUnavailable,
                "El proveedor de pagos no está disponible. Intentá nuevamente en unos minutos.",
                failure);
        }

        return new IdempotentResult<PaymentDto>(MapToDto(payment), created);
    }

    /// <summary>Debe invocarse con la membresía bloqueada.</summary>
    private async Task<Payment> CreatePendingCheckoutPaymentAsync(
        CreatePaymentCheckoutRequest request,
        Guid payerPersonId,
        string? idempotencyKey,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var membership = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.Id == request.MembershipId)
            .Select(m => new { m.Status, m.Currency, m.PurchasePrice })
            .FirstAsync(cancellationToken);

        if (membership.Status == MembershipStatus.Cancelled)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.ObligationCancelled,
                "No se puede pagar una membresía cancelada.");
        }

        var netPaid = await GetNetPaidAsync(request.MembershipId, cancellationToken);
        var pendingBalance = membership.PurchasePrice - netPaid;
        if (pendingBalance <= 0m)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.ObligationOverpaid,
                "La membresía no tiene saldo pendiente.",
                new { membership.PurchasePrice, TotalPaidAmount = netPaid, PendingBalance = 0m });
        }

        var now = _gymCalendar.UtcNow;

        var openCheckouts = await _dbContext.Payments
            .Where(p => p.MembershipId == request.MembershipId && p.IsActive &&
                        p.Provider != PaymentProvider.Manual && p.Status == PaymentStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var open in openCheckouts)
        {
            if (!open.IsCheckoutExpired(now))
            {
                throw ConflictException.WithCode(
                    PaymentErrorCodes.CheckoutAlreadyOpen,
                    "La membresía ya tiene un pago en línea en curso. Cancelalo o esperá a que venza para iniciar otro.",
                    new { PaymentId = open.Id, open.ExpiresAtUtc });
            }

            ExpireCheckout(open, now);
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = payerPersonId,
            MembershipId = request.MembershipId,
            Amount = pendingBalance,
            Currency = membership.Currency,
            // El medio real lo elige el pagador en el proveedor
            Method = PaymentMethod.DigitalWallet,
            Provider = request.Provider,
            Status = PaymentStatus.Pending,
            RegisteredByUserId = userId,
            IdempotencyKey = idempotencyKey,
            PaidAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_paymentsOptions.CheckoutExpirationMinutes),
            CreatedAtUtc = now,
            IsActive = true
        };

        _dbContext.Payments.Add(payment);
        AddAuditLog("PAYMENT_CHECKOUT_INITIATED", payment.Id, userId, oldValues: null, Snapshot(payment));
        return payment;
    }

    // ---- Vencimiento ----

    public async Task<int> ExpireCheckoutsAsync(CancellationToken cancellationToken = default)
    {
        var now = _gymCalendar.UtcNow;
        var candidateIds = await _dbContext.Payments
            .AsNoTracking()
            .Where(p => p.IsActive && p.Provider != PaymentProvider.Manual &&
                        p.Status == PaymentStatus.Pending && p.ExpiresAtUtc != null && p.ExpiresAtUtc <= now)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var id in candidateIds)
        {
            await using var transaction = await BeginOwnTransactionAsync(cancellationToken);
            var payment = await LockAndLoadAsync(id, targetMembershipId: null, cancellationToken);

            // Pudo confirmarse o cancelarse mientras se esperaba el bloqueo
            if (payment.IsCheckoutExpired(now))
            {
                ExpireCheckout(payment, now);
                await SaveChangesMappingConcurrencyAsync(cancellationToken);
                expired++;
            }

            await CommitAsync(transaction, cancellationToken);
        }

        return expired;
    }

    private void ExpireCheckout(Payment payment, DateTime nowUtc)
    {
        var before = Snapshot(payment);
        payment.Cancel(ExpiredCheckoutReason, nowUtc);
        AddAuditLog("PAYMENT_EXPIRED", payment.Id, userId: null, before, Snapshot(payment, ExpiredCheckoutReason), systemAction: true);
    }

    // ---- Webhooks ----

    public async Task<WebhookProcessingResult> ProcessWebhookAsync(
        PaymentProvider provider,
        WebhookRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_providerResolver == null || !_providerResolver.TryResolve(provider, out var paymentProvider))
        {
            throw NotFoundException.WithCode(PaymentErrorCodes.ProviderNotSupported, $"El proveedor de pagos {provider} no está disponible.");
        }

        // La firma se verifica antes de tocar la base: un intento no verificado no deja registro alguno.
        var webhookEvent = paymentProvider!.VerifyAndParseWebhook(request);
        if (webhookEvent == null ||
            string.IsNullOrWhiteSpace(webhookEvent.ExternalEventId) ||
            string.IsNullOrWhiteSpace(webhookEvent.ExternalTransactionId) ||
            webhookEvent.ExternalEventId.Length > MaxExternalIdLength ||
            webhookEvent.ExternalTransactionId.Length > MaxExternalIdLength)
        {
            _logger.LogWarning("Notificación de {Provider} rechazada: firma inválida, fuera de tolerancia o mal formada.", provider);
            return WebhookProcessingResult.InvalidSignature;
        }

        if (await WebhookEventExistsAsync(provider, webhookEvent.ExternalEventId, cancellationToken))
        {
            return WebhookProcessingResult.AlreadyProcessed;
        }

        // El contenido de la notificación no es fuente de datos: el estado real se consulta al proveedor.
        var snapshot = await FetchSnapshotAsync(
            () => paymentProvider.GetPaymentAsync(webhookEvent.ExternalTransactionId, cancellationToken), provider);

        var now = _gymCalendar.UtcNow;
        var paymentId = snapshot == null ? null : await FindPaymentIdForSnapshotAsync(provider, snapshot, cancellationToken);

        try
        {
            await using var transaction = await BeginOwnTransactionAsync(cancellationToken);

            Payment? payment = null;
            if (paymentId.HasValue)
            {
                payment = await LockAndLoadAsync(paymentId.Value, targetMembershipId: null, cancellationToken);

                // Otra copia del mismo evento pudo procesarse mientras se esperaba el bloqueo
                if (await WebhookEventExistsAsync(provider, webhookEvent.ExternalEventId, cancellationToken))
                {
                    await CommitAsync(transaction, cancellationToken);
                    return WebhookProcessingResult.AlreadyProcessed;
                }
            }

            var storedEvent = new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                ExternalEventId = webhookEvent.ExternalEventId,
                ExternalTransactionId = webhookEvent.ExternalTransactionId,
                EventType = webhookEvent.EventType,
                PaymentId = payment?.Id,
                ReceivedAtUtc = now,
                ProcessedAtUtc = now,
                CreatedAtUtc = now,
                IsActive = true
            };
            _dbContext.PaymentWebhookEvents.Add(storedEvent);

            WebhookProcessingResult result;
            if (snapshot == null)
            {
                storedEvent.Status = PaymentWebhookEventStatus.Ignored;
                storedEvent.Detail = "El proveedor no conoce la transacción";
                result = WebhookProcessingResult.Ignored;
            }
            else
            {
                storedEvent.Status = PaymentWebhookEventStatus.Processed;
                storedEvent.Detail = snapshot.RawStatus;
                await ApplyProviderSnapshotAsync(payment, snapshot, provider, cancellationToken);

                if (payment != null)
                {
                    AddAuditLog(
                        "PAYMENT_WEBHOOK_PROCESSED",
                        payment.Id,
                        userId: null,
                        oldValues: null,
                        new { webhookEvent.ExternalEventId, snapshot.ExternalTransactionId, ExternalStatus = snapshot.RawStatus, Status = payment.Status.ToString() },
                        systemAction: true);
                }

                result = WebhookProcessingResult.Processed;
            }

            await SaveChangesMappingConcurrencyAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return result;
        }
        catch (DbUpdateException) when (paymentId == null)
        {
            // Sin pago que bloquear, dos copias simultáneas chocan contra los índices únicos: la otra ya lo registró
            DetachPendingGatewayRecords();
            return WebhookProcessingResult.AlreadyProcessed;
        }
    }

    private Task<bool> WebhookEventExistsAsync(PaymentProvider provider, string externalEventId, CancellationToken cancellationToken) =>
        _dbContext.PaymentWebhookEvents.AnyAsync(e => e.Provider == provider && e.ExternalEventId == externalEventId, cancellationToken);

    private async Task<Guid?> FindPaymentIdForSnapshotAsync(
        PaymentProvider provider,
        ExternalPaymentSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(snapshot.PaymentReference, out var referencedId))
        {
            var byReference = await _dbContext.Payments
                .AsNoTracking()
                .Where(p => p.Id == referencedId && p.IsActive && p.Provider == provider)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (byReference.HasValue)
            {
                return byReference;
            }
        }

        return await _dbContext.Payments
            .AsNoTracking()
            .Where(p => p.IsActive && p.Provider == provider && p.ExternalTransactionId == snapshot.ExternalTransactionId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Regla única para webhooks y conciliación. Sólo confirma un pago pendiente cuyo importe y moneda coinciden;
    /// cualquier otra diferencia se registra como discrepancia sin modificar importes ni estados confirmados.
    /// Debe invocarse con el pago bloqueado.
    /// </summary>
    private async Task ApplyProviderSnapshotAsync(
        Payment? payment,
        ExternalPaymentSnapshot snapshot,
        PaymentProvider provider,
        CancellationToken cancellationToken)
    {
        var approved = snapshot.Status == ExternalPaymentStatus.Approved;
        var moneyWasCollected = approved ||
                                snapshot.Status is ExternalPaymentStatus.Refunded or ExternalPaymentStatus.ChargedBack;

        if (payment == null)
        {
            if (approved)
            {
                await AddDiscrepancyAsync(PaymentDiscrepancyType.UnknownPayment, null, snapshot, provider, cancellationToken);
            }

            return;
        }

        var sameTransaction = string.Equals(payment.ExternalTransactionId, snapshot.ExternalTransactionId, StringComparison.Ordinal);

        if (payment.Status == PaymentStatus.Pending)
        {
            if (!approved)
            {
                // Rechazado o en proceso: el pagador puede reintentar en el mismo checkout
                return;
            }

            if (!string.Equals(payment.Currency, snapshot.Currency, StringComparison.OrdinalIgnoreCase))
            {
                await AddDiscrepancyAsync(PaymentDiscrepancyType.CurrencyMismatch, payment, snapshot, provider, cancellationToken);
                return;
            }

            if (payment.Amount != snapshot.Amount)
            {
                await AddDiscrepancyAsync(PaymentDiscrepancyType.AmountMismatch, payment, snapshot, provider, cancellationToken);
                return;
            }

            // El cobro ya ocurrió en el proveedor: se confirma aunque la membresía se haya cubierto entretanto
            var overpays = payment.MembershipId.HasValue &&
                           await WouldOverpayMembershipAsync(payment.MembershipId.Value, payment.Amount, cancellationToken);

            payment.Complete(snapshot.ExternalTransactionId, _gymCalendar.UtcNow);

            if (overpays)
            {
                await AddDiscrepancyAsync(PaymentDiscrepancyType.Overpayment, payment, snapshot, provider, cancellationToken);
            }

            return;
        }

        if (payment.IsConfirmed)
        {
            if (!sameTransaction)
            {
                if (approved)
                {
                    await AddDiscrepancyAsync(PaymentDiscrepancyType.DuplicateCharge, payment, snapshot, provider, cancellationToken);
                }

                return;
            }

            // Devuelto en el proveedor por fuera del sistema: lo que excede a lo registrado y a lo que está en curso
            var pendingRefunds = await GetPendingRefundsAmountAsync(payment.Id, cancellationToken);
            if (moneyWasCollected && snapshot.RefundedAmount > payment.RefundedAmount + pendingRefunds)
            {
                await AddDiscrepancyAsync(
                    PaymentDiscrepancyType.ExternalRefund, payment, snapshot with { Amount = snapshot.RefundedAmount }, provider, cancellationToken);
            }

            return;
        }

        // Cancelled o Failed (incluido el vencido)
        if (approved)
        {
            await AddDiscrepancyAsync(PaymentDiscrepancyType.ApprovedAfterTerminalState, payment, snapshot, provider, cancellationToken);
        }
    }

    private async Task<bool> WouldOverpayMembershipAsync(Guid membershipId, decimal amount, CancellationToken cancellationToken)
    {
        var purchasePrice = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.Id == membershipId)
            .Select(m => m.PurchasePrice)
            .FirstAsync(cancellationToken);

        return await GetNetPaidAsync(membershipId, cancellationToken) + amount > purchasePrice;
    }

    private async Task<decimal> GetPendingRefundsAmountAsync(Guid paymentId, CancellationToken cancellationToken) =>
        await _dbContext.PaymentRefunds
            .Where(r => r.PaymentId == paymentId && r.IsActive && r.Status == PaymentRefundStatus.Pending)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

    /// <summary>No duplica: a lo sumo una discrepancia abierta por causa (los índices únicos son la última barrera).</summary>
    private async Task AddDiscrepancyAsync(
        PaymentDiscrepancyType type,
        Payment? payment,
        ExternalPaymentSnapshot snapshot,
        PaymentProvider provider,
        CancellationToken cancellationToken)
    {
        var alreadyOpen = payment != null
            ? await _dbContext.PaymentDiscrepancies.AnyAsync(
                d => d.PaymentId == payment.Id && d.Type == type && d.Status == PaymentDiscrepancyStatus.Open, cancellationToken)
            : await _dbContext.PaymentDiscrepancies.AnyAsync(
                d => d.PaymentId == null && d.Provider == provider && d.Type == type &&
                     d.ExternalTransactionId == snapshot.ExternalTransactionId && d.Status == PaymentDiscrepancyStatus.Open,
                cancellationToken);

        if (alreadyOpen)
        {
            return;
        }

        var discrepancy = PaymentDiscrepancy.Create(
            type, provider, payment, snapshot.ExternalTransactionId, snapshot.Amount, snapshot.Currency, snapshot.RawStatus, _gymCalendar.UtcNow);
        _dbContext.PaymentDiscrepancies.Add(discrepancy);

        AddAuditLog(
            "PAYMENT_DISCREPANCY_DETECTED",
            discrepancy.Id,
            userId: null,
            oldValues: null,
            DiscrepancySnapshot(discrepancy),
            entityName: nameof(PaymentDiscrepancy),
            systemAction: true);
    }

    // ---- Conciliación ----

    public async Task<PaymentDto> ReconcileAsync(Guid id, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var peek = await _dbContext.Payments
            .AsNoTracking()
            .Where(p => p.Id == id && p.IsActive)
            .Select(p => new { p.Provider, p.ExternalTransactionId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw PaymentNotFound();

        if (peek.Provider == PaymentProvider.Manual)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.PaymentNotExternal,
                "Sólo se concilian pagos de un proveedor externo.");
        }

        var provider = ResolveProvider(peek.Provider);
        var snapshot = await FetchSnapshotAsync(
            () => string.IsNullOrWhiteSpace(peek.ExternalTransactionId)
                ? provider.FindPaymentByReferenceAsync(id.ToString(), cancellationToken)
                : provider.GetPaymentAsync(peek.ExternalTransactionId, cancellationToken),
            peek.Provider);

        await using var transaction = await BeginOwnTransactionAsync(cancellationToken);
        var payment = await LockAndLoadAsync(id, targetMembershipId: null, cancellationToken);
        var before = Snapshot(payment);

        if (snapshot != null)
        {
            // Primero las devoluciones que quedaron pendientes: así lo que el proveedor ya devolvió por pedido
            // nuestro no se toma por una devolución externa.
            await ResolvePendingRefundsAsync(payment, snapshot, cancellationToken);
            await ApplyProviderSnapshotAsync(payment, snapshot, peek.Provider, cancellationToken);
        }

        AddAuditLog(
            "PAYMENT_RECONCILED",
            payment.Id,
            NullIfEmpty(currentUserId),
            before,
            new { Status = payment.Status.ToString(), payment.RefundedAmount, ExternalStatus = snapshot?.RawStatus, snapshot?.ExternalTransactionId });

        await SaveChangesMappingConcurrencyAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return MapToDto(payment);
    }

    /// <summary>
    /// Una devolución pendiente es una que se pidió al proveedor y cuyo resultado no llegó a registrarse.
    /// Se completa si lo que el proveedor informa como devuelto la cubre; si no, se da por fallida.
    /// </summary>
    private async Task ResolvePendingRefundsAsync(Payment payment, ExternalPaymentSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!payment.IsConfirmed ||
            !string.Equals(payment.ExternalTransactionId, snapshot.ExternalTransactionId, StringComparison.Ordinal))
        {
            return;
        }

        var pending = await _dbContext.PaymentRefunds
            .Where(r => r.PaymentId == payment.Id && r.IsActive && r.Status == PaymentRefundStatus.Pending)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var now = _gymCalendar.UtcNow;
        var unmatched = snapshot.RefundedAmount - payment.RefundedAmount;

        foreach (var refund in pending)
        {
            if (refund.Amount <= unmatched)
            {
                payment.CompleteRefund(refund, externalRefundId: null, now);
                unmatched -= refund.Amount;
                AddAuditLog("PAYMENT_REFUNDED", payment.Id, userId: null, oldValues: null, RefundSnapshot(payment, refund), systemAction: true);
            }
            else
            {
                refund.Fail("El proveedor no registra el reintegro", now);
                AddAuditLog("PAYMENT_REFUND_FAILED", payment.Id, userId: null, oldValues: null, RefundSnapshot(payment, refund), systemAction: true);
            }
        }
    }

    // ---- Devoluciones contra el proveedor ----

    /// <summary>
    /// Recibe el pago ya bloqueado dentro de <paramref name="transaction"/>. Reserva el importe con una devolución
    /// pendiente, llama al proveedor fuera de la transacción y registra el resultado en otra.
    /// </summary>
    private async Task<IdempotentResult<PaymentRefundDto>> CreateExternalRefundAsync(
        Payment payment,
        IDbContextTransaction? transaction,
        CreatePaymentRefundRequest request,
        string reason,
        string? idempotencyKey,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(payment.Provider);
        var userId = NullIfEmpty(currentUserId);

        if (idempotencyKey != null)
        {
            var existing = await _dbContext.PaymentRefunds
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.PaymentId == payment.Id && r.IdempotencyKey == idempotencyKey, cancellationToken);

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

        // 1. Las devoluciones en curso reservan su importe: dos pedidos simultáneos no pueden ir ambos al proveedor.
        var pendingRefunds = await GetPendingRefundsAmountAsync(payment.Id, cancellationToken);
        var available = payment.NetAmount - pendingRefunds;
        if (request.Amount > available)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.RefundAmountExceeded,
                $"La devolución supera el importe disponible para devolver ({available:0.00} {payment.Currency}).",
                new { payment.Amount, payment.RefundedAmount, PendingRefunds = pendingRefunds, Available = available });
        }

        var refund = payment.RequestRefund(
            request.Amount, reason, userId, pendingRefunds, idempotencyKey, request.Notes, _gymCalendar.UtcNow);
        _dbContext.PaymentRefunds.Add(refund);
        AddAuditLog("PAYMENT_REFUND_REQUESTED", payment.Id, userId, oldValues: null, RefundSnapshot(payment, refund));

        var externalTransactionId = payment.ExternalTransactionId
            ?? throw InvalidTransition("El pago no tiene una transacción del proveedor para devolver.");

        await SaveChangesMappingConcurrencyAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        // 2. Llamada al proveedor, sin transacción ni bloqueos. El Id de la devolución evita reintegros duplicados.
        ExternalRefundResult result;
        try
        {
            result = await provider.RequestRefundAsync(externalTransactionId, refund.Amount, refund.Id.ToString(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "El proveedor {Provider} no pudo procesar la devolución {RefundId}.", payment.Provider, refund.Id);
            result = new ExternalRefundResult(false, null, "El proveedor de pagos no respondió");
        }

        // 3. Se registra el resultado.
        await using (var resultTransaction = await BeginOwnTransactionAsync(cancellationToken))
        {
            payment = await LockAndLoadAsync(payment.Id, targetMembershipId: null, cancellationToken);
            refund = await _dbContext.PaymentRefunds.FirstAsync(r => r.Id == refund.Id, cancellationToken);
            var refundEntry = _dbContext.PaymentRefunds.Entry(refund);
            if (refundEntry.State == EntityState.Unchanged)
            {
                await refundEntry.ReloadAsync(cancellationToken);
            }

            // Una conciliación pudo resolverla mientras se esperaba al proveedor
            if (refund.Status == PaymentRefundStatus.Pending)
            {
                var before = Snapshot(payment);
                if (result.Succeeded)
                {
                    payment.CompleteRefund(refund, result.ExternalRefundId, _gymCalendar.UtcNow);
                    AddAuditLog("PAYMENT_REFUNDED", payment.Id, userId, before, RefundSnapshot(payment, refund));
                }
                else
                {
                    refund.Fail(result.FailureReason ?? "El proveedor rechazó el reintegro", _gymCalendar.UtcNow);
                    AddAuditLog("PAYMENT_REFUND_FAILED", payment.Id, userId, before, RefundSnapshot(payment, refund));
                }

                await SaveChangesMappingConcurrencyAsync(cancellationToken);
            }

            await CommitAsync(resultTransaction, cancellationToken);
        }

        if (refund.Status == PaymentRefundStatus.Failed)
        {
            throw new ExternalServiceException(
                PaymentErrorCodes.ProviderRefundFailed,
                $"El proveedor de pagos no realizó el reintegro: {refund.FailureReason}.");
        }

        return new IdempotentResult<PaymentRefundDto>(MapToDto(refund), Created: true);
    }

    // ---- Discrepancias ----

    public async Task<PagedResult<PaymentDiscrepancyDto>> GetDiscrepanciesAsync(
        PaymentDiscrepancyStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _dbContext.PaymentDiscrepancies.AsNoTracking().Where(d => d.IsActive);
        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(d => d.DetectedAtUtc)
            .ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PaymentDiscrepancyDto>(items.Select(MapToDto).ToList(), totalCount, page, pageSize);
    }

    public async Task<PaymentDiscrepancyDto> ResolveDiscrepancyAsync(
        Guid id,
        ResolvePaymentDiscrepancyRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new ValidationException("Las notas de resolución son obligatorias.");
        }

        if (request.Notes.Trim().Length > 1000)
        {
            throw new ValidationException("Las notas de resolución no pueden superar los 1000 caracteres.");
        }

        var discrepancy = await _dbContext.PaymentDiscrepancies
            .FirstOrDefaultAsync(d => d.Id == id && d.IsActive, cancellationToken)
            ?? throw NotFoundException.WithCode(PaymentErrorCodes.DiscrepancyNotFound, "Discrepancia no encontrada.");

        if (discrepancy.Status != PaymentDiscrepancyStatus.Open)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.DiscrepancyAlreadyResolved,
                "La discrepancia ya fue resuelta.");
        }

        var userId = NullIfEmpty(currentUserId);
        var before = DiscrepancySnapshot(discrepancy);
        discrepancy.Resolve(request.Notes, userId, _gymCalendar.UtcNow);
        AddAuditLog(
            "PAYMENT_DISCREPANCY_RESOLVED", discrepancy.Id, userId, before, DiscrepancySnapshot(discrepancy), nameof(PaymentDiscrepancy));

        await SaveChangesMappingConcurrencyAsync(cancellationToken);
        return MapToDto(discrepancy);
    }

    // ---- Auxiliares ----

    private IPaymentProvider ResolveProvider(PaymentProvider provider)
    {
        if (provider == PaymentProvider.Manual || _providerResolver == null)
        {
            throw ConflictException.WithCode(
                PaymentErrorCodes.ProviderNotSupported,
                $"El proveedor de pagos {provider} no está disponible.");
        }

        return _providerResolver.Resolve(provider);
    }

    /// <summary>Un fallo del proveedor al consultar es transitorio: se informa para que la operación se reintente.</summary>
    private async Task<ExternalPaymentSnapshot?> FetchSnapshotAsync(Func<Task<ExternalPaymentSnapshot?>> fetch, PaymentProvider provider)
    {
        try
        {
            return await fetch();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "No se pudo consultar la transacción en {Provider}.", provider);
            throw new ExternalServiceException(
                PaymentErrorCodes.ProviderUnavailable,
                "El proveedor de pagos no está disponible. Intentá nuevamente en unos minutos.",
                ex);
        }
    }

    private async Task<decimal> GetNetPaidAsync(Guid membershipId, CancellationToken cancellationToken) =>
        await _dbContext.Payments
            .Where(p => p.MembershipId == membershipId && p.IsActive &&
                        (p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.PartiallyRefunded))
            .SumAsync(p => (decimal?)(p.Amount - p.RefundedAmount), cancellationToken) ?? 0m;

    /// <summary>Tras un choque de unicidad, lo que no se guardó no debe arrastrarse a un guardado posterior.</summary>
    private void DetachPendingGatewayRecords()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Added &&
                                 e.Entity is PaymentWebhookEvent or PaymentDiscrepancy or AuditLog)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static object RefundSnapshot(Payment payment, PaymentRefund refund) => new
    {
        payment.RefundedAmount,
        Status = payment.Status.ToString(),
        RefundId = refund.Id,
        RefundAmount = refund.Amount,
        refund.Currency,
        RefundStatus = refund.Status.ToString(),
        refund.Reason,
        refund.ExternalRefundId,
        refund.FailureReason
    };

    private static object DiscrepancySnapshot(PaymentDiscrepancy d) => new
    {
        d.PaymentId,
        Provider = d.Provider.ToString(),
        Type = d.Type.ToString(),
        Status = d.Status.ToString(),
        d.InternalAmount,
        d.InternalCurrency,
        d.InternalStatus,
        d.ExternalAmount,
        d.ExternalCurrency,
        d.ExternalStatus,
        d.ExternalTransactionId,
        d.ResolutionNotes
    };

    private static PaymentDiscrepancyDto MapToDto(PaymentDiscrepancy d) =>
        new(
            d.Id,
            d.PaymentId,
            d.Provider,
            d.Type,
            d.Status,
            d.InternalAmount,
            d.InternalCurrency,
            d.InternalStatus,
            d.ExternalAmount,
            d.ExternalCurrency,
            d.ExternalStatus,
            d.ExternalTransactionId,
            d.DetectedAtUtc,
            d.ResolvedAtUtc,
            d.ResolvedByUserId,
            d.ResolutionNotes);
}
