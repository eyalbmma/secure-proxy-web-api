namespace MoD.SecureProxy.Data.Entities;

public sealed class HeaderRule
{
    public int Id { get; set; }
    public int OperationId { get; set; }
    public string HeaderName { get; set; } = string.Empty;

    public Operation Operation { get; set; } = null!;
}
