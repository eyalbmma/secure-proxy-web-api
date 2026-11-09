namespace MoD.SecureProxy.Data.Entities;

public sealed class RequestLog
{
    public long Id { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string OperationKey { get; set; } = string.Empty;
    public int? DestinationId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long? DurationMilliseconds { get; set; }
    public bool ForwardingOccurred { get; set; }
    public int? UpstreamStatusCode { get; set; }
    public int? ProxyStatusCode { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
}
