using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;

namespace SmartGym.Domain.UnitTests.Promotions;

internal static class PromotionTestSupport
{
    public static readonly DateOnly Today = new(2026, 6, 15);

    public sealed class StubGymCalendar : IGymCalendar
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateTime UtcNow { get; set; } = new(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc);
        public DateOnly Today { get; set; } = PromotionTestSupport.Today;
        public DateTime ToUtc(DateOnly date, TimeOnly time) =>
            new(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
        public DateTimeOffset ToLocal(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        public (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to) =>
            (ToUtc(from, TimeOnly.MinValue), ToUtc(to.AddDays(1), TimeOnly.MinValue));
    }

    public sealed class StubCurrentUserService : ICurrentUserService
    {
        private readonly HashSet<string> _roles;

        public StubCurrentUserService(Guid? personId, params string[] roles)
        {
            PersonId = personId;
            _roles = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        }

        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? PersonId { get; }
        public string? Email => "user@test.com";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles => _roles.ToList();
        public bool IsInRole(string role) => _roles.Contains(role);
    }

    public static DbContextOptions<SmartGymDbContext> InMemoryOptions(string? databaseName = null) =>
        new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    public static SmartGymDbContext CreateDbContext() => new(InMemoryOptions());

    public static Promotion NewPromotion(string code, Action<Promotion>? configure = null)
    {
        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 20m,
            ValidFrom = new DateOnly(2026, 6, 1),
            ValidTo = new DateOnly(2026, 6, 30),
            MaxUsesPerPerson = 1,
            IsActive = true
        };
        configure?.Invoke(promo);
        return promo;
    }

    public static CreatePromotionRequest ValidCreateRequest(string code = "BIENVENIDA") => new(
        Code: code,
        Name: "Bienvenida",
        Description: null,
        ConditionType: PromotionConditionType.CouponCode,
        BenefitType: PromotionBenefitType.PercentageDiscount,
        BenefitValue: 20m,
        ValidFrom: new DateOnly(2026, 6, 1),
        ValidTo: new DateOnly(2026, 6, 30),
        MaxUsesTotal: 100,
        MaxUsesPerPerson: 1,
        ApplicablePlanId: null);

    public static UpdatePromotionRequest UpdateRequestFrom(PromotionDto dto) => new(
        Code: dto.Code,
        Name: dto.Name,
        Description: dto.Description,
        ConditionType: dto.ConditionType,
        BenefitType: dto.BenefitType,
        BenefitValue: dto.BenefitValue,
        ValidFrom: dto.ValidFrom,
        ValidTo: dto.ValidTo,
        MaxUsesTotal: dto.MaxUsesTotal,
        MaxUsesPerPerson: dto.MaxUsesPerPerson,
        ApplicablePlanId: dto.ApplicablePlanId,
        Version: dto.Version);
}
