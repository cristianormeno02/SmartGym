using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Infrastructure;

public class DomainEventDispatchTests
{
    private record DummyEvent(string Message) : IDomainEvent;
    private record CascadingEvent(string Message) : IDomainEvent;

    private class DummyEntity : BaseEntity, IHasDomainEvents
    {
        public string Name { get; set; } = string.Empty;

        private readonly List<IDomainEvent> _domainEvents = new();
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        public void AddEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
        public void ClearDomainEvents() => _domainEvents.Clear();
    }

    private class TestDbContext : SmartGymDbContext
    {
        public DbSet<DummyEntity> Dummies => Set<DummyEntity>();

        public TestDbContext(DbContextOptions<SmartGymDbContext> options, IServiceProvider? serviceProvider = null)
            : base(options, serviceProvider)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<DummyEntity>();
        }
    }

    [Fact]
    public async Task Events_AreDispatched_BeforePersisting_AndCleared()
    {
        var services = new ServiceCollection();
        var dispatched = false;

        services.AddScoped<IDomainEventHandler<DummyEvent>>(_ => new ActionHandler<DummyEvent>(_ =>
        {
            dispatched = true;
            return Task.CompletedTask;
        }));

        var serviceProvider = services.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContext(options, serviceProvider);
        var dummy = new DummyEntity { Name = "Test" };
        dummy.AddEvent(new DummyEvent("Hello"));
        context.Dummies.Add(dummy);

        await context.SaveChangesAsync();

        Assert.True(dispatched);
        Assert.Empty(dummy.DomainEvents);
    }

    [Fact]
    public async Task HandlerChanges_AreSavedInSameSaveChanges()
    {
        var services = new ServiceCollection();

        services.AddScoped<IDomainEventHandler<DummyEvent>>(sp => new ActionHandler<DummyEvent>(evt =>
        {
            // handler modifies another entity in the same DbContext
            var db = sp.GetRequiredService<TestDbContext>();
            db.Dummies.Add(new DummyEntity { Name = "CreatedByHandler" });
            return Task.CompletedTask;
        }));

        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var serviceCollection = new ServiceCollection();
        TestDbContext? context = null;
        serviceCollection.AddSingleton(sp => context!);
        serviceCollection.AddScoped<IDomainEventHandler<DummyEvent>>(sp => new ActionHandler<DummyEvent>(evt =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Dummies.Add(new DummyEntity { Name = "CreatedByHandler" });
            return Task.CompletedTask;
        }));

        var sp = serviceCollection.BuildServiceProvider();
        context = new TestDbContext(options, sp);

        var dummy = new DummyEntity { Name = "Initial" };
        dummy.AddEvent(new DummyEvent("Trigger"));
        context.Dummies.Add(dummy);

        await context.SaveChangesAsync();

        var count = await context.Dummies.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CascadingEvents_AreProcessed()
    {
        var serviceCollection = new ServiceCollection();
        var step1 = false;
        var step2 = false;

        serviceCollection.AddScoped<IDomainEventHandler<DummyEvent>>(sp => new ActionHandler<DummyEvent>(evt =>
        {
            step1 = true;
            var db = sp.GetRequiredService<TestDbContext>();
            var second = new DummyEntity { Name = "Second" };
            second.AddEvent(new CascadingEvent("Cascading"));
            db.Dummies.Add(second);
            return Task.CompletedTask;
        }));

        serviceCollection.AddScoped<IDomainEventHandler<CascadingEvent>>(sp => new ActionHandler<CascadingEvent>(evt =>
        {
            step2 = true;
            return Task.CompletedTask;
        }));

        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        TestDbContext? context = null;
        serviceCollection.AddSingleton(sp => context!);

        var sp = serviceCollection.BuildServiceProvider();
        context = new TestDbContext(options, sp);

        var dummy = new DummyEntity { Name = "Root" };
        dummy.AddEvent(new DummyEvent("Start"));
        context.Dummies.Add(dummy);

        await context.SaveChangesAsync();

        Assert.True(step1);
        Assert.True(step2);
    }

    [Fact]
    public async Task HandlerException_PreventsPersisting()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped<IDomainEventHandler<DummyEvent>>(_ => new ActionHandler<DummyEvent>(_ =>
            throw new InvalidOperationException("Handler exploded")));

        var sp = serviceCollection.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContext(options, sp);
        var dummy = new DummyEntity { Name = "ShouldNotPersist" };
        dummy.AddEvent(new DummyEvent("Boom"));
        context.Dummies.Add(dummy);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Equal(0, await context.Dummies.CountAsync());
    }

    [Fact]
    public async Task MoreThanFiveRounds_ThrowsInvalidOperationException()
    {
        var serviceCollection = new ServiceCollection();
        TestDbContext? context = null;
        serviceCollection.AddSingleton(sp => context!);

        serviceCollection.AddScoped<IDomainEventHandler<DummyEvent>>(sp => new ActionHandler<DummyEvent>(evt =>
        {
            var next = new DummyEntity { Name = "Next" };
            next.AddEvent(new DummyEvent("Loop"));
            context!.Dummies.Add(next);
            return Task.CompletedTask;
        }));

        var sp = serviceCollection.BuildServiceProvider();
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        context = new TestDbContext(options, sp);

        var root = new DummyEntity { Name = "Root" };
        root.AddEvent(new DummyEvent("Loop"));
        context.Dummies.Add(root);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    private record DummyPostCommitEvent(string Message) : IPostCommitDomainEvent;

    private class ScopedMarker
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
    }

    private class TransactionalTestDbContext : TestDbContext
    {
        public bool SimulateActiveTransaction { get; set; }

        public TransactionalTestDbContext(DbContextOptions<SmartGymDbContext> options, IServiceProvider? serviceProvider = null)
            : base(options, serviceProvider)
        {
        }

        protected override bool HasActiveExplicitTransaction => SimulateActiveTransaction || base.HasActiveExplicitTransaction;
    }

    [Fact]
    public async Task PostCommitEvent_IsNotDispatchedBeforePersisting_AndDispatchedAfterSaveWhenNoTransaction()
    {
        var serviceCollection = new ServiceCollection();
        var preCommitRan = false;
        var postCommitRanDuringPreCommit = false;
        var postCommitRan = false;
        var countInNewScopeDuringPostCommit = -1;

        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        serviceCollection.AddScoped(_ => new TestDbContext(options));

        serviceCollection.AddScoped<IDomainEventHandler<DummyEvent>>(_ => new ActionHandler<DummyEvent>(_ =>
        {
            preCommitRan = true;
            postCommitRanDuringPreCommit = postCommitRan;
            return Task.CompletedTask;
        }));

        serviceCollection.AddScoped<IDomainEventHandler<DummyPostCommitEvent>>(sp => new ActionHandler<DummyPostCommitEvent>(async _ =>
        {
            postCommitRan = true;
            var freshDb = sp.GetRequiredService<TestDbContext>();
            countInNewScopeDuringPostCommit = await freshDb.Dummies.CountAsync();
        }));

        var sp = serviceCollection.BuildServiceProvider();
        using var context = new TestDbContext(options, sp);

        var dummy = new DummyEntity { Name = "PersistedFirst" };
        dummy.AddEvent(new DummyEvent("Pre"));
        dummy.AddEvent(new DummyPostCommitEvent("Post"));
        context.Dummies.Add(dummy);

        await context.SaveChangesAsync();

        Assert.True(preCommitRan);
        Assert.False(postCommitRanDuringPreCommit);
        Assert.True(postCommitRan);
        Assert.Equal(1, countInNewScopeDuringPostCommit);
    }

    [Fact]
    public async Task PostCommitEvent_WithActiveTransaction_IsDeferredUntilCommit_AndDiscardedOnRollback()
    {
        var dispatchedMessages = new List<string>();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped<IDomainEventHandler<DummyPostCommitEvent>>(_ => new ActionHandler<DummyPostCommitEvent>(evt =>
        {
            dispatchedMessages.Add(evt.Message);
            return Task.CompletedTask;
        }));

        var sp = serviceCollection.BuildServiceProvider();
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TransactionalTestDbContext(options, sp)
        {
            SimulateActiveTransaction = true
        };

        var dummy1 = new DummyEntity { Name = "TxCommit" };
        dummy1.AddEvent(new DummyPostCommitEvent("CommittedMsg"));
        context.Dummies.Add(dummy1);

        await context.SaveChangesAsync();

        // Still inside uncommitted transaction: must not have dispatched yet
        Assert.Empty(dispatchedMessages);
        Assert.Single(context.PendingPostCommitEvents);

        // Commit dispatches the queued event
        await context.DispatchPostCommitEventsAsync();
        Assert.Single(dispatchedMessages);
        Assert.Equal("CommittedMsg", dispatchedMessages[0]);
        Assert.Empty(context.PendingPostCommitEvents);

        // Now queue another event and simulate rollback
        var dummy2 = new DummyEntity { Name = "TxRollback" };
        dummy2.AddEvent(new DummyPostCommitEvent("RolledBackMsg"));
        context.Dummies.Add(dummy2);

        await context.SaveChangesAsync();
        Assert.Single(context.PendingPostCommitEvents);

        context.ClearPostCommitEvents();
        await context.DispatchPostCommitEventsAsync();

        Assert.Single(dispatchedMessages);
        Assert.Empty(context.PendingPostCommitEvents);
    }

    [Fact]
    public async Task PostCommitEvent_HandlerException_IsSwallowedAndLogged_DoesNotStopOtherHandlers_AndRunsInIsolatedScopes()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddScoped<ScopedMarker>();

        var scopeIds = new List<Guid>();
        var secondHandlerRan = false;

        serviceCollection.AddScoped<IDomainEventHandler<DummyPostCommitEvent>>(sp => new ActionHandler<DummyPostCommitEvent>(_ =>
        {
            scopeIds.Add(sp.GetRequiredService<ScopedMarker>().InstanceId);
            throw new InvalidOperationException("First post-commit handler failed");
        }));

        serviceCollection.AddScoped<IDomainEventHandler<DummyPostCommitEvent>>(sp => new ActionHandler<DummyPostCommitEvent>(_ =>
        {
            scopeIds.Add(sp.GetRequiredService<ScopedMarker>().InstanceId);
            secondHandlerRan = true;
            return Task.CompletedTask;
        }));

        var sp = serviceCollection.BuildServiceProvider();
        var callerScopeId = sp.GetRequiredService<ScopedMarker>().InstanceId;

        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new TestDbContext(options, sp);
        var dummy = new DummyEntity { Name = "Resilient" };
        dummy.AddEvent(new DummyPostCommitEvent("Notify"));
        context.Dummies.Add(dummy);

        // Must not throw despite first handler throwing
        var saved = await context.SaveChangesAsync();

        Assert.Equal(1, saved);
        Assert.True(secondHandlerRan);
        Assert.Equal(2, scopeIds.Count);
        Assert.NotEqual(callerScopeId, scopeIds[0]);
        Assert.NotEqual(callerScopeId, scopeIds[1]);
        Assert.NotEqual(scopeIds[0], scopeIds[1]);
    }

    private class ActionHandler<T> : IDomainEventHandler<T> where T : IDomainEvent
    {
        private readonly Func<T, Task> _action;
        public ActionHandler(Func<T, Task> action) => _action = action;
        public Task HandleAsync(T domainEvent, CancellationToken cancellationToken = default) => _action(domainEvent);
    }
}

