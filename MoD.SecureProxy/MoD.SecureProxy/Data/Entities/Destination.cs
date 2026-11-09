namespace MoD.SecureProxy.Data.Entities;

public sealed class Destination
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int RequestTimeoutMilliseconds { get; set; } = 30_000;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<Operation> Operations { get; set; } = new List<Operation>();
}
