using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SmartGym.Infrastructure.Persistence.Interceptors;

public sealed class PostCommitDomainEventInterceptor : DbTransactionInterceptor
{
    public static readonly PostCommitDomainEventInterceptor Instance = new();

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is SmartGymDbContext dbContext)
        {
            dbContext.DispatchPostCommitEventsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        base.TransactionCommitted(transaction, eventData);
    }

    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is SmartGymDbContext dbContext)
        {
            await dbContext.DispatchPostCommitEventsAsync(cancellationToken);
        }

        await base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is SmartGymDbContext dbContext)
        {
            dbContext.ClearPostCommitEvents();
        }

        base.TransactionRolledBack(transaction, eventData);
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is SmartGymDbContext dbContext)
        {
            dbContext.ClearPostCommitEvents();
        }

        return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        if (eventData.Context is SmartGymDbContext dbContext)
        {
            dbContext.ClearPostCommitEvents();
        }

        base.TransactionFailed(transaction, eventData);
    }

    public override Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is SmartGymDbContext dbContext)
        {
            dbContext.ClearPostCommitEvents();
        }

        return base.TransactionFailedAsync(transaction, eventData, cancellationToken);
    }
}
