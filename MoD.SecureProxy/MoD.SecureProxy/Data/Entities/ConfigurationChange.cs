namespace MoD.SecureProxy.Data.Entities;

/// <summary>
/// History of non-secret operation configuration values changed through the
/// configuration update service. Request payloads and encryption key material
/// must never be written here.
/// </summary>
public sealed class ConfigurationChange
{
    public long Id { get; set; }
    public string EntityType { get; set; } = "Operation";
    public int EntityId { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
    public string ChangedFieldsJson { get; set; } = "[]";
    public string OldValuesJson { get; set; } = "{}";
    public string NewValuesJson { get; set; } = "{}";
}
