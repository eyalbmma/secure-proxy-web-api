using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MoD.SecureProxy.Data.Entities;

namespace MoD.SecureProxy.Tests.TestInfrastructure;

public sealed class AuditSaveChangesFailureInterceptor(AuditFailureMode failureMode) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var shouldFail = false;
        if (eventData.Context is { } context)
        {
            var requestLogEntries = context.ChangeTracker.Entries<RequestLog>();
            shouldFail = failureMode switch
            {
                AuditFailureMode.FailInitialWrite => requestLogEntries.Any(entry => entry.State == EntityState.Added),
                AuditFailureMode.FailCompletionWrite => requestLogEntries.Any(entry => entry.State == EntityState.Modified),
                _ => false
            };
        }

        if (shouldFail)
        {
            throw new DbUpdateException("Simulated audit persistence failure.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
