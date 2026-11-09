using Microsoft.EntityFrameworkCore;
using MoD.SecureProxy.Data;
using MoD.SecureProxy.Data.Entities;

namespace MoD.SecureProxy.Services;

public sealed class OperationLookup
{
    private readonly ProxyDbContext _dbContext;

    public OperationLookup(ProxyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Operation?> FindByKeyAsync(string operationKey, CancellationToken cancellationToken) =>
        _dbContext.Operations
            .AsNoTracking()
            .Include(operation => operation.Destination)
            .Include(operation => operation.ParameterRules)
            .Include(operation => operation.EncryptionProfile)
            .SingleOrDefaultAsync(
                operation => operation.OperationKey == operationKey,
                cancellationToken);
}
