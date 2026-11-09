namespace MoD.SecureProxy.Data.Entities;

public sealed class Operation
{
    public int Id { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public int DestinationId { get; set; }
    public string HttpMethod { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public long MaxRequestSizeBytes { get; set; } = 1_000_000;
    public bool AllowUnknownParameters { get; set; }
    public bool RequiresEncryption { get; set; }
    public int? EncryptionProfileId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Destination Destination { get; set; } = null!;
    public EncryptionProfile? EncryptionProfile { get; set; }
    public ICollection<ParameterRule> ParameterRules { get; set; } = new List<ParameterRule>();
    public ICollection<HeaderRule> HeaderRules { get; set; } = new List<HeaderRule>();
}
