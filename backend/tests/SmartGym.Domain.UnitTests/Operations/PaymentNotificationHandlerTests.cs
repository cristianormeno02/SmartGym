using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Modules.Operations.Handlers;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Operations;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentNotificationHandlerTests : IAsyncDisposable
{
    private readonly TestGymCalendar _calendar = new(new DateOnly(2026, 10, 12));
    private readonly SmartGymDbContext _db = new(MembershipTestSupport.InMemoryOptions());
    private readonly NotifyPayerOnPaymentLifecycleHandler _handler;
    private readonly Person _payer = Person.Create("Martín", "Gómez", "martin.pagos@test.com");

    public PaymentNotificationHandlerTests()
    {
        _db.People.Add(_payer);
        _db.SaveChanges();
        _handler = new NotifyPayerOnPaymentLifecycleHandler(_db, _calendar);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task EachPaymentEvent_CreatesItsInAppNotificationLinkedToThePayment()
    {
        var paymentId = Guid.NewGuid();

        await _handler.HandleAsync(new PaymentConfirmedNotificationEvent(paymentId, _payer.Id, 25000m, "ARS"));
        await _handler.HandleAsync(new PaymentFailedNotificationEvent(Guid.NewGuid(), _payer.Id, 18000m, "ARS", "Comprobante inválido"));
        await _handler.HandleAsync(new PaymentRefundedNotificationEvent(paymentId, Guid.NewGuid(), _payer.Id, 10000m, "ARS"));

        var messages = await _db.NotificationMessages.AsNoTracking().Where(n => n.RecipientPersonId == _payer.Id).ToListAsync();

        var confirmed = Assert.Single(messages, n => n.Type == NotificationType.PaymentConfirmed);
        Assert.Equal(NotificationCategory.Payments, confirmed.Category);
        Assert.Equal("Payment", confirmed.RelatedEntityType);
        Assert.Equal(paymentId, confirmed.RelatedEntityId);
        Assert.Contains("25000.00 ARS", confirmed.Body);

        var failed = Assert.Single(messages, n => n.Type == NotificationType.PaymentFailed);
        Assert.Contains("18000.00 ARS", failed.Body);
        Assert.Contains("Comprobante inválido", failed.Body);

        var refunded = Assert.Single(messages, n => n.Type == NotificationType.PaymentRefunded);
        Assert.Contains("10000.00 ARS", refunded.Body);
    }

    [Fact]
    public async Task RepeatedEvent_DoesNotDuplicate_ButEachRefundOfAPaymentIsNotified()
    {
        var paymentId = Guid.NewGuid();
        var confirmed = new PaymentConfirmedNotificationEvent(paymentId, _payer.Id, 30000m, "ARS");
        var firstRefund = new PaymentRefundedNotificationEvent(paymentId, Guid.NewGuid(), _payer.Id, 10000m, "ARS");
        var secondRefund = new PaymentRefundedNotificationEvent(paymentId, Guid.NewGuid(), _payer.Id, 5000m, "ARS");

        await _handler.HandleAsync(confirmed);
        await _handler.HandleAsync(confirmed);
        await _handler.HandleAsync(firstRefund);
        await _handler.HandleAsync(firstRefund);
        await _handler.HandleAsync(secondRefund);

        var messages = await _db.NotificationMessages.AsNoTracking().ToListAsync();
        Assert.Single(messages, n => n.Type == NotificationType.PaymentConfirmed);
        Assert.Equal(2, messages.Count(n => n.Type == NotificationType.PaymentRefunded));
    }
}
