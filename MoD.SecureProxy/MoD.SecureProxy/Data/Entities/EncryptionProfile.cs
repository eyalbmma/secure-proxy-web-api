namespace MoD.SecureProxy.Data.Entities;

public sealed class EncryptionProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeySourceReference { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }

    public ICollection<Operation> Operations { get; set; } = new List<Operation>();
}
