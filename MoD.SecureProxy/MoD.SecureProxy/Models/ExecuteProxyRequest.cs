using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoD.SecureProxy.Models;

/// <summary>
/// The assessment requires clientId and urlId. The precise meaning and type
/// of urlId remain an explicit assumption because the supplied example is malformed.
/// </summary>
public sealed class ExecuteProxyRequest
{
    [Required]
    [StringLength(150)]
    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    [Required]
    [StringLength(256)]
    [JsonPropertyName("urlId")]
    public string UrlId { get; init; } = string.Empty;

    [Required]
    [JsonPropertyName("json")]
    public JsonElement? Json { get; init; }
}
