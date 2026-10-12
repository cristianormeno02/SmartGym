using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;

using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence.Interceptors;

namespace SmartGym.Infrastructure.Persistence;

public class SmartGymDbContext : DbContext, ISmartGymDbContext
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly Queue<IPostCommitDomainEvent> _postCommitEvents = new();

    public SmartGymDbContext(DbContextOptions<SmartGymDbContext> options, IServiceProvider? serviceProvider = null)
        : base(options)
    {
        _serviceProvider = serviceProvider;
    }

    public IReadOnlyCollection<IPostCommitDomainEvent> PendingPostCommitEvents => _postCommitEvents;

    protected virtual bool HasActiveExplicitTransaction =>
        Database.IsRelational() && Database.CurrentTransaction != null;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(PostCommitDomainEventInterceptor.Instance);
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
    public DbSet<ReservationStatusChange> ReservationStatusChanges => Set<ReservationStatusChange>();
    public DbSet<FamilyGroup> FamilyGroups => Set<FamilyGroup>();
    public DbSet<FamilyGroupMember> FamilyGroupMembers => Set<FamilyGroupMember>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionRedemption> PromotionRedemptions => Set<PromotionRedemption>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<NotificationMessage> NotificationMessages => Set<NotificationMessage>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
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

    public virtual async Task LockPromotionForUpdateAsync(Guid promotionId, CancellationToken cancellationToken = default)
    {
        if (!Database.IsNpgsql())
        {
            return;
        }

        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("El bloqueo de la promoción requiere una transacción activa.");
        }

        await Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Promotions\" WHERE \"Id\" = {promotionId} FOR UPDATE",
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

    public virtual async Task<List<NotificationDelivery>> ClaimPendingNotificationDeliveriesAsync(
        int batchSize,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (Database.IsNpgsql())
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

            var pendingStatus = (int)NotificationDeliveryStatus.Pending;
            var claimed = await NotificationDeliveries
                .FromSqlInterpolated($@"
                    SELECT * FROM ""NotificationDeliveries""
                    WHERE ""IsActive"" = TRUE
                      AND ""Status"" = {pendingStatus}
                      AND (""NextAttemptAtUtc"" IS NULL OR ""NextAttemptAtUtc"" <= {nowUtc})
                    ORDER BY ""NextAttemptAtUtc"" ASC NULLS FIRST, ""CreatedAtUtc"" ASC
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);

            if (claimed.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return claimed;
            }

            foreach (var delivery in claimed)
            {
                delivery.MarkProcessing(nowUtc);
            }

            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claimed;
        }

        var inMemoryDeliveries = await NotificationDeliveries
            .Where(d => d.IsActive &&
                        d.Status == NotificationDeliveryStatus.Pending &&
                        (d.NextAttemptAtUtc == null || d.NextAttemptAtUtc <= nowUtc))
            .OrderBy(d => d.NextAttemptAtUtc)
            .ThenBy(d => d.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (inMemoryDeliveries.Count == 0)
        {
            return inMemoryDeliveries;
        }

        foreach (var delivery in inMemoryDeliveries)
        {
            delivery.MarkProcessing(nowUtc);
        }

        await SaveChangesAsync(cancellationToken);
        return inMemoryDeliveries;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceProvider != null)
        {
            await DispatchDomainEventsAsync(cancellationToken);
        }

        int result;
        try
        {
            result = await SaveChangesBreakingReservationCyclesAsync(cancellationToken);
        }
        catch
        {
            if (!HasActiveExplicitTransaction)
            {
                ClearPostCommitEvents();
            }

            throw;
        }

        if (_serviceProvider != null && !HasActiveExplicitTransaction)
        {
            await DispatchPostCommitEventsAsync(cancellationToken);
        }

        return result;
    }

    private async Task<int> SaveChangesBreakingReservationCyclesAsync(CancellationToken cancellationToken)
    {
        if (!Database.IsRelational())
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        var trackedReservationIds = ChangeTracker.Entries<Reservation>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity.Id)
            .ToHashSet();

        if (trackedReservationIds.Count == 0)
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        var deferredMovements = new List<(MembershipCreditMovement Movement, Guid ReservationId)>();
        foreach (var entry in ChangeTracker.Entries<MembershipCreditMovement>())
        {
            if (entry.State == EntityState.Added &&
                entry.Entity.ReservationId.HasValue &&
                trackedReservationIds.Contains(entry.Entity.ReservationId.Value))
            {
                deferredMovements.Add((entry.Entity, entry.Entity.ReservationId.Value));
                entry.Entity.ReservationId = null;
                entry.Entity.Reservation = null;
            }
        }

        var deferredRedemptions = new List<(PromotionRedemption Redemption, Guid ReservationId)>();
        foreach (var entry in ChangeTracker.Entries<PromotionRedemption>())
        {
            if (entry.State == EntityState.Added &&
                entry.Entity.ReservationId.HasValue &&
                trackedReservationIds.Contains(entry.Entity.ReservationId.Value))
            {
                deferredRedemptions.Add((entry.Entity, entry.Entity.ReservationId.Value));
                entry.Entity.ReservationId = null;
                entry.Entity.Reservation = null;
            }
        }

        var firstPass = await base.SaveChangesAsync(cancellationToken);

        if (deferredMovements.Count == 0 && deferredRedemptions.Count == 0)
        {
            return firstPass;
        }

        foreach (var (movement, reservationId) in deferredMovements)
        {
            movement.ReservationId = reservationId;
        }

        foreach (var (redemption, reservationId) in deferredRedemptions)
        {
            redemption.ReservationId = reservationId;
        }

        var secondPass = await base.SaveChangesAsync(cancellationToken);
        return firstPass + secondPass;
    }

    public void ClearPostCommitEvents()
    {
        _postCommitEvents.Clear();
    }

    public async Task DispatchPostCommitEventsAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceProvider == null || _postCommitEvents.Count == 0)
        {
            _postCommitEvents.Clear();
            return;
        }

        var events = _postCommitEvents.ToArray();
        _postCommitEvents.Clear();

        var logger = _serviceProvider.GetService<ILogger<SmartGymDbContext>>();
        var scopeFactory = _serviceProvider.GetService<IServiceScopeFactory>();

        foreach (var domainEvent in events)
        {
            var eventType = domainEvent.GetType();
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync));
            if (method == null)
            {
                continue;
            }

            List<object?> handlersSnapshot;
            if (scopeFactory != null)
            {
                await using var discoveryScope = scopeFactory.CreateAsyncScope();
                handlersSnapshot = discoveryScope.ServiceProvider.GetServices(handlerType).ToList();
            }
            else
            {
                handlersSnapshot = _serviceProvider.GetServices(handlerType).ToList();
            }

            var handlerCount = handlersSnapshot.Count;
            for (var i = 0; i < handlerCount; i++)
            {
                try
                {
                    if (scopeFactory != null)
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var scopedHandlers = scope.ServiceProvider.GetServices(handlerType).ToList();
                        if (i >= scopedHandlers.Count || scopedHandlers[i] == null)
                        {
                            continue;
                        }

                        var task = (Task)method.Invoke(scopedHandlers[i], new object[] { domainEvent, cancellationToken })!;
                        await task;
                    }
                    else
                    {
                        var handler = handlersSnapshot[i];
                        if (handler == null)
                        {
                            continue;
                        }

                        var task = (Task)method.Invoke(handler, new object[] { domainEvent, cancellationToken })!;
                        await task;
                    }
                }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
                {
                    logger?.LogError(
                        ex.InnerException,
                        "Error al ejecutar handler post-commit para el evento {EventType}.",
                        eventType.Name);
                }
                catch (Exception ex)
                {
                    logger?.LogError(
                        ex,
                        "Error al ejecutar handler post-commit para el evento {EventType}.",
                        eventType.Name);
                }
            }
        }
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var rounds = 0;
        const int maxRounds = 5;

        while (true)
        {
            var entitiesWithEvents = ChangeTracker.Entries<IHasDomainEvents>()
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

            var eventsToDispatch = new List<IDomainEvent>();
            foreach (var entity in entitiesWithEvents)
            {
                eventsToDispatch.AddRange(entity.DomainEvents);
                entity.ClearDomainEvents();
            }

            foreach (var domainEvent in eventsToDispatch)
            {
                if (domainEvent is IPostCommitDomainEvent postCommitEvent)
                {
                    _postCommitEvents.Enqueue(postCommitEvent);
                    continue;
                }

                var eventType = domainEvent.GetType();
                var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
                var handlers = _serviceProvider!.GetServices(handlerType);

                foreach (var handler in handlers)
                {
                    if (handler != null)
                    {
                        var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync));
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

