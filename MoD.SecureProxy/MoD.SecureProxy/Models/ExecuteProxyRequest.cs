using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoD.SecureProxy.Models;

/// <summary>
/// The request contains an operation key and its operation-specific JSON.
/// A configured path parameter such as urlId is supplied inside json.
/// </summary>
public sealed class ExecuteProxyRequest
{
    [Required]
    [StringLength(150)]
    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    [Required]
    [JsonPropertyName("json")]
    public JsonElement? Json { get; init; }
}