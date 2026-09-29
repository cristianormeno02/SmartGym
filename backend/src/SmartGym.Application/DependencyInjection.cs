using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Application.Modules.Identity.Services;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Application.Modules.Reservations.Services;

namespace SmartGym.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IRecurringScheduleService, RecurringScheduleService>();
        services.AddScoped<IClassSessionService, ClassSessionService>();

        services.AddScoped<IMembershipPlanService, MembershipPlanService>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<ICreditLedgerService, CreditLedgerService>();

        services.AddScoped<IReservationService, ReservationService>();

        services.AddScoped<IFamilyGroupService, FamilyGroupService>();
        services.AddScoped<IPromotionService, PromotionService>();

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IMedicalCertificateService, MedicalCertificateService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IDashboardService, DashboardService>();

        return services;
    }
}
