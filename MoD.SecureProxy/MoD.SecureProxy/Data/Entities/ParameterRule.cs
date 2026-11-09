namespace MoD.SecureProxy.Data.Entities;

public sealed class ParameterRule
{
    public int Id { get; set; }
    public int OperationId { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string DataType { get; set; } = "string";
    public int? MaximumLength { get; set; }
    public decimal? MinimumValue { get; set; }
    public decimal? MaximumValue { get; set; }
    public ParameterLocation Location { get; set; } = ParameterLocation.Body;

    public Operation Operation { get; set; } = null!;
}
