using MoD.SecureProxy.Data;
using MoD.SecureProxy.Data.Entities;

namespace MoD.SecureProxy.Services;

public sealed class RequestAuditor
{
    private readonly ProxyDbContext _dbContext;
    private readonly ILogger<RequestAuditor> _logger;

    public RequestAuditor(ProxyDbContext dbContext, ILogger<RequestAuditor> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<RequestLog?> TryStartAsync(
        string correlationId,
        string operationKey,
        CancellationToken cancellationToken)
    {
        var log = new RequestLog
        {
            CorrelationId = correlationId,
            OperationKey = operationKey,
            StartedAtUtc = DateTime.UtcNow,
            Outcome = "Pending",
            ForwardingOccurred = false
        };

        try
        {
            _dbContext.RequestLogs.Add(log);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return log;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Could not persist the initial audit record. CorrelationId: {CorrelationId}, ExceptionType: {ExceptionType}",
                correlationId,
                exception.GetType().Name);
            return null;
        }
    }

    public async Task TryCompleteAsync(
        RequestLog log,
        int proxyStatusCode,
        string outcome,
        string? failureReason,
        bool forwardingOccurred)
    {
        log.CompletedAtUtc = DateTime.UtcNow;
        log.DurationMilliseconds = (long)(log.CompletedAtUtc.Value - log.StartedAtUtc).TotalMilliseconds;
        log.ProxyStatusCode = proxyStatusCode;
        log.Outcome = outcome;
        log.FailureReason = failureReason;
        log.ForwardingOccurred = forwardingOccurred;

        try
        {
            // Completion is attempted even if the caller disconnected after the audit start was persisted.
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Could not complete the audit record. CorrelationId: {CorrelationId}, ExceptionType: {ExceptionType}",
                log.CorrelationId,
                exception.GetType().Name);
        }
    }
}
