using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Application.Modules.Identity.Services;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.People.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Application.Modules.Reservations.Services;

namespace SmartGym.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGymCalendar, GymCalendar>();

        services.AddScoped<IPeopleService, PeopleService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IRecurringScheduleService, RecurringScheduleService>();
        services.AddScoped<IClassSessionGenerator, ClassSessionGenerator>();
        services.AddScoped<IClassSessionScheduleSync, ClassSessionScheduleSync>();
        services.AddScoped<IClassSessionService, ClassSessionService>();

        services.AddScoped<IMembershipPlanService, MembershipPlanService>();
        services.AddScoped<ICreditLedgerService, CreditLedgerService>();
        services.AddScoped<IMembershipEligibilityService, MembershipEligibilityService>();
        services.AddScoped<IMembershipActivationService, MembershipActivationService>();
        services.AddScoped<IMembershipService, MembershipService>();

        services.AddScoped<IReservationService, ReservationService>();

        services.AddScoped<IFamilyGroupService, FamilyGroupService>();
        services.AddScoped<IPromotionService, PromotionService>();

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IMedicalCertificateService, MedicalCertificateService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IDashboardService, DashboardService>();

        // Domain Event Handlers
        services.AddScoped<SmartGym.Application.Modules.Reservations.Handlers.CancelReservationsOnSessionVoidedHandler>();
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Activities.ClassSessionSuspended>>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Reservations.Handlers.CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Activities.ClassSessionCancelled>>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Reservations.Handlers.CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Activities.ClassSessionRescheduled>>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Reservations.Handlers.CancelReservationsOnSessionVoidedHandler>());

        services.AddScoped<SmartGym.Application.Modules.Operations.Handlers.NotifyStudentsOnSessionChangeHandler>();
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Activities.ClassSessionSuspended>>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Operations.Handlers.NotifyStudentsOnSessionChangeHandler>());
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Activities.ClassSessionCancelled>>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Operations.Handlers.NotifyStudentsOnSessionChangeHandler>());
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Activities.ClassSessionRescheduled>>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Operations.Handlers.NotifyStudentsOnSessionChangeHandler>());
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Activities.ClassSessionRoomChanged>>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Operations.Handlers.NotifyStudentsOnSessionChangeHandler>());

        services.AddScoped<SmartGym.Application.Modules.Memberships.Handlers.ActivateMembershipOnPaymentCompletedHandler>();
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Operations.PaymentCompletedEvent>>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Memberships.Handlers.ActivateMembershipOnPaymentCompletedHandler>());

        services.AddScoped<SmartGym.Application.Modules.Reservations.Handlers.CancelReservationsOnMembershipSuspendedHandler>();
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Memberships.MembershipSuspendedEvent>>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Reservations.Handlers.CancelReservationsOnMembershipSuspendedHandler>());

        services.AddScoped<SmartGym.Application.Modules.Reservations.Handlers.CancelFutureReservationsOnMembershipCancelledHandler>();
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Memberships.MembershipCancelledEvent>>(sp =>
            sp.GetRequiredService<SmartGym.Application.Modules.Reservations.Handlers.CancelFutureReservationsOnMembershipCancelledHandler>());

        return services;
    }
}
