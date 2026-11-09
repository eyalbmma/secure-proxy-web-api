using Microsoft.EntityFrameworkCore;
using MoD.SecureProxy.Data;
using MoD.SecureProxy.Data.Entities;

namespace MoD.SecureProxy.Services;

/// <summary>
/// Updates operation configuration for an authorized caller supplied by a future
/// management boundary. This service intentionally does not expose an HTTP endpoint.
/// </summary>
public sealed class OperationConfigurationUpdater
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE"
    };

    private readonly ProxyDbContext _dbContext;

    public OperationConfigurationUpdater(ProxyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OperationConfigurationUpdateResult> UpdateAsync(
        int operationId,
        byte[] expectedRowVersion,
        OperationConfigurationUpdate update,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(update);
        if (validationError is not null)
        {
            return OperationConfigurationUpdateResult.Invalid(validationError);
        }

        var operation = await _dbContext.Operations
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);

        if (operation is null)
        {
            return OperationConfigurationUpdateResult.NotFound();
        }

        // Fast precondition check gives deterministic conflict handling before changes are made.
        // EF Core also includes this value in the SQL UPDATE predicate, closing the race
        // between this read and SaveChangesAsync when using SQL Server rowversion.
        if (expectedRowVersion is null ||
            !operation.RowVersion.AsSpan().SequenceEqual(expectedRowVersion))
        {
            return OperationConfigurationUpdateResult.Conflict();
        }

        var versionProperty = _dbContext.Entry(operation).Property(item => item.RowVersion);
        versionProperty.OriginalValue = expectedRowVersion.ToArray();

        operation.IsActive = update.IsActive;
        operation.HttpMethod = update.HttpMethod.Trim().ToUpperInvariant();
        operation.RelativePath = update.RelativePath.Trim();
        operation.MaxRequestSizeBytes = update.MaxRequestSizeBytes;
        operation.AllowUnknownParameters = update.AllowUnknownParameters;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return OperationConfigurationUpdateResult.Updated(operation.RowVersion.ToArray());
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationConfigurationUpdateResult.Conflict();
        }
    }

    private static string? Validate(OperationConfigurationUpdate update)
    {
        if (update is null)
        {
            return "invalid_update";
        }

        if (string.IsNullOrWhiteSpace(update.HttpMethod) ||
            !SupportedMethods.Contains(update.HttpMethod.Trim()))
        {
            return "unsupported_http_method";
        }

        if (update.MaxRequestSizeBytes <= 0)
        {
            return "invalid_request_size_limit";
        }

        var path = update.RelativePath?.Trim();
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > 2048 ||
            path.Contains('\\') ||
            path.Contains('?') ||
            path.Contains('#') ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            Uri.TryCreate(path, UriKind.Absolute, out _) ||
            path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            return "invalid_relative_path";
        }

        return null;
    }
}

public sealed record OperationConfigurationUpdate(
    bool IsActive,
    string HttpMethod,
    string RelativePath,
    long MaxRequestSizeBytes,
    bool AllowUnknownParameters);

public enum OperationConfigurationUpdateStatus
{
    Updated,
    NotFound,
    Conflict,
    Invalid
}

public sealed record OperationConfigurationUpdateResult(
    OperationConfigurationUpdateStatus Status,
    byte[]? RowVersion = null,
    string? ErrorCode = null)
{
    public static OperationConfigurationUpdateResult Updated(byte[] rowVersion) =>
        new(OperationConfigurationUpdateStatus.Updated, rowVersion);

    public static OperationConfigurationUpdateResult NotFound() =>
        new(OperationConfigurationUpdateStatus.NotFound);

    public static OperationConfigurationUpdateResult Conflict() =>
        new(OperationConfigurationUpdateStatus.Conflict);

    public static OperationConfigurationUpdateResult Invalid(string errorCode) =>
        new(OperationConfigurationUpdateStatus.Invalid, ErrorCode: errorCode);
}
