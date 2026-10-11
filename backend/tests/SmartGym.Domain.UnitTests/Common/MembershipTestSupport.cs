using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Application.Modules.Reservations.Handlers;
using SmartGym.Domain.Events.Memberships;
using SmartGym.Infrastructure.Persistence;

namespace SmartGym.Domain.UnitTests.Common;

public sealed class TestGymCalendar : IGymCalendar
{
    public TestGymCalendar(DateOnly today, DateTime? utcNow = null)
    {
        Today = today;
        UtcNow = utcNow ?? today.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
    }

    public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
    public DateOnly Today { get; set; }
    public DateTime UtcNow { get; set; }
    public DateTime ToUtc(DateOnly date, TimeOnly time) =>
        new(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
    public DateTimeOffset ToLocal(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    public (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to) =>
        (ToUtc(from, TimeOnly.MinValue), ToUtc(to.AddDays(1), TimeOnly.MinValue));
}

public static class MembershipTestSupport
{
    public static DbContextOptions<SmartGymDbContext> InMemoryOptions() =>
        new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    /// <summary>
    /// Crea un contexto que despacha los eventos de dominio de membresías a sus handlers,
    /// igual que el contenedor de la aplicación. Sin esto SaveChanges no despacha ningún evento.
    /// </summary>
    public static SmartGymDbContext CreateDbContextWithMembershipEventHandlers(IGymCalendar? calendar = null)
    {
        SmartGymDbContext? db = null;
        var services = new ServiceCollection();
        services.AddScoped<IDomainEventHandler<MembershipSuspendedEvent>>(_ =>
            new CancelReservationsOnMembershipSuspendedHandler(db!, gymCalendar: calendar));
        services.AddScoped<IDomainEventHandler<MembershipCancelledEvent>>(_ =>
            new CancelFutureReservationsOnMembershipCancelledHandler(db!));

        db = new SmartGymDbContext(InMemoryOptions(), services.BuildServiceProvider());
        return db;
    }

    public static MembershipService CreateMembershipService(
        SmartGymDbContext db,
        IGymCalendar calendar,
        IPromotionService? promotionService = null)
    {
        var gymOptions = Options.Create(new GymOptions { DefaultCurrency = "ARS" });
        var creditLedger = new CreditLedgerService(db, calendar);
        var auditService = new AuditService(db);

        return new MembershipService(
            db,
            creditLedger,
            new MembershipActivationService(db, creditLedger, calendar, auditService),
            new MembershipEligibilityService(db, calendar, gymOptions),
            new PaymentService(db),
            promotionService ?? new PromotionService(db, calendar),
            calendar,
            gymOptions,
            Options.Create(new MembershipsOptions()),
            auditService);
    }
}
