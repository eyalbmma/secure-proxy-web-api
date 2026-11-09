using System.Text.Json;
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
        string changedBy,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(update, changedBy);
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

        var oldValues = new OperationConfigurationValues(
            operation.IsActive,
            operation.HttpMethod,
            operation.RelativePath,
            operation.MaxRequestSizeBytes,
            operation.AllowUnknownParameters);
        var newValues = new OperationConfigurationValues(
            update.IsActive,
            update.HttpMethod.Trim().ToUpperInvariant(),
            update.RelativePath.Trim(),
            update.MaxRequestSizeBytes,
            update.AllowUnknownParameters);

        var changedFields = GetChangedFields(oldValues, newValues);
        if (changedFields.Count == 0)
        {
            return OperationConfigurationUpdateResult.Updated(operation.RowVersion.ToArray());
        }

        operation.IsActive = newValues.IsActive;
        operation.HttpMethod = newValues.HttpMethod;
        operation.RelativePath = newValues.RelativePath;
        operation.MaxRequestSizeBytes = newValues.MaxRequestSizeBytes;
        operation.AllowUnknownParameters = newValues.AllowUnknownParameters;

        _dbContext.ConfigurationChanges.Add(new ConfigurationChange
        {
            EntityType = "Operation",
            EntityId = operation.Id,
            ChangedBy = changedBy.Trim(),
            ChangedAtUtc = DateTime.UtcNow,
            ChangedFieldsJson = JsonSerializer.Serialize(changedFields),
            OldValuesJson = JsonSerializer.Serialize(oldValues),
            NewValuesJson = JsonSerializer.Serialize(newValues)
        });

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

    private static string? Validate(OperationConfigurationUpdate update, string changedBy)
    {
        if (update is null)
        {
            return "invalid_update";
        }

        if (string.IsNullOrWhiteSpace(changedBy) || changedBy.Trim().Length > 150)
        {
            return "invalid_changed_by";
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
            path.Contains('%') ||
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

    private static List<string> GetChangedFields(
        OperationConfigurationValues oldValues,
        OperationConfigurationValues newValues)
    {
        var changed = new List<string>();
        if (oldValues.IsActive != newValues.IsActive) changed.Add(nameof(Operation.IsActive));
        if (oldValues.HttpMethod != newValues.HttpMethod) changed.Add(nameof(Operation.HttpMethod));
        if (oldValues.RelativePath != newValues.RelativePath) changed.Add(nameof(Operation.RelativePath));
        if (oldValues.MaxRequestSizeBytes != newValues.MaxRequestSizeBytes)
            changed.Add(nameof(Operation.MaxRequestSizeBytes));
        if (oldValues.AllowUnknownParameters != newValues.AllowUnknownParameters)
            changed.Add(nameof(Operation.AllowUnknownParameters));
        return changed;
    }

    private sealed record OperationConfigurationValues(
        bool IsActive,
        string HttpMethod,
        string RelativePath,
        long MaxRequestSizeBytes,
        bool AllowUnknownParameters);
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
