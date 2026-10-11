using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;

using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Entities.Reservations;

namespace SmartGym.Infrastructure.Persistence;

public class SmartGymDbContext : DbContext, ISmartGymDbContext
{
    private readonly IServiceProvider? _serviceProvider;

    public SmartGymDbContext(DbContextOptions<SmartGymDbContext> options, IServiceProvider? serviceProvider = null)
        : base(options)
    {
        _serviceProvider = serviceProvider;
    }

    public DbSet<Person> People => Set<Person>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<PersonRole> PersonRoles => Set<PersonRole>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<ActivityMedia> ActivityMedias => Set<ActivityMedia>();
    public DbSet<RecurringSchedule> RecurringSchedules => Set<RecurringSchedule>();
    public DbSet<ClassSession> ClassSessions => Set<ClassSession>();
    public DbSet<ClassSessionStaffChange> ClassSessionStaffChanges => Set<ClassSessionStaffChange>();
    public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
    public DbSet<MembershipPlanActivity> MembershipPlanActivities => Set<MembershipPlanActivity>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MembershipAllowedActivity> MembershipAllowedActivities => Set<MembershipAllowedActivity>();
    public DbSet<MembershipStatusChange> MembershipStatusChanges => Set<MembershipStatusChange>();
    public DbSet<MembershipSuspension> MembershipSuspensions => Set<MembershipSuspension>();
    public DbSet<MembershipCreditMovement> MembershipCreditMovements => Set<MembershipCreditMovement>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<FamilyGroup> FamilyGroups => Set<FamilyGroup>();
    public DbSet<FamilyGroupMember> FamilyGroupMembers => Set<FamilyGroupMember>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionRedemption> PromotionRedemptions => Set<PromotionRedemption>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<NotificationMessage> NotificationMessages => Set<NotificationMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public virtual async Task LockClassSessionForUpdateAsync(Guid classSessionId, CancellationToken cancellationToken = default)
    {
        // Proveedores sin bloqueo de filas (p. ej. InMemory en pruebas) no aplican.
        if (!Database.IsNpgsql())
        {
            return;
        }

        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("El bloqueo de la clase requiere una transacción activa.");
        }

        await Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"ClassSessions\" WHERE \"Id\" = {classSessionId} FOR UPDATE",
            cancellationToken);
    }

    public virtual async Task LockPersonForUpdateAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        if (!Database.IsNpgsql())
        {
            return;
        }

        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("El bloqueo de la persona requiere una transacción activa.");
        }

        await Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"People\" WHERE \"Id\" = {personId} FOR UPDATE",
            cancellationToken);
    }

    public virtual async Task LockMembershipForUpdateAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        if (!Database.IsNpgsql())
        {
            return;
        }

        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("El bloqueo de la membresía requiere una transacción activa.");
        }

        await Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Memberships\" WHERE \"Id\" = {membershipId} FOR UPDATE",
            cancellationToken);
    }

    public virtual async Task LockStudentMembershipsForUpdateAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        if (!Database.IsNpgsql())
        {
            return;
        }

        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("El bloqueo de las membresías del alumno requiere una transacción activa.");
        }

        await Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Memberships\" WHERE \"StudentId\" = {studentId} FOR UPDATE",
            cancellationToken);
    }

    public virtual async Task DeferClassSessionOverlapConstraintsAsync(CancellationToken cancellationToken = default)
    {
        if (!Database.IsNpgsql())
        {
            return;
        }

        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("Diferir las restricciones de solapamiento requiere una transacción activa.");
        }

        await Database.ExecuteSqlRawAsync(
            "SET CONSTRAINTS \"EX_ClassSessions_Room_NoOverlap\", \"EX_ClassSessions_Instructor_NoOverlap\" DEFERRED",
            cancellationToken);
    }

    public virtual async Task EnforceDeferredConstraintsAsync(CancellationToken cancellationToken = default)
    {
        if (!Database.IsNpgsql())
        {
            return;
        }

        // Fuerza la verificación ahora (y no en el COMMIT) para poder traducir las violaciones a 409.
        await Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceProvider != null)
        {
            await DispatchDomainEventsAsync(cancellationToken);
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var rounds = 0;
        const int maxRounds = 5;

        while (true)
        {
            var entitiesWithEvents = ChangeTracker.Entries<SmartGym.Domain.Common.IHasDomainEvents>()
                .Select(e => e.Entity)
                .Where(e => e.DomainEvents.Any())
                .ToList();

            if (!entitiesWithEvents.Any())
            {
                break;
            }

            rounds++;
            if (rounds > maxRounds)
            {
                throw new InvalidOperationException($"Exceeded maximum domain event dispatch rounds ({maxRounds}). Possible circular event trigger.");
            }

            var eventsToDispatch = new List<SmartGym.Domain.Common.IDomainEvent>();
            foreach (var entity in entitiesWithEvents)
            {
                eventsToDispatch.AddRange(entity.DomainEvents);
                entity.ClearDomainEvents();
            }

            foreach (var domainEvent in eventsToDispatch)
            {
                var eventType = domainEvent.GetType();
                var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
                var handlers = _serviceProvider!.GetServices(handlerType);

                foreach (var handler in handlers)
                {
                    if (handler != null)
                    {
                        var method = handlerType.GetMethod(nameof(IDomainEventHandler<SmartGym.Domain.Common.IDomainEvent>.HandleAsync));
                        if (method != null)
                        {
                            try
                            {
                                var task = (Task)method.Invoke(handler, new object[] { domainEvent, cancellationToken })!;
                                await task;
                            }
                            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
                            {
                                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                            }
                        }
                    }
                }
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartGymDbContext).Assembly);
    }
}
