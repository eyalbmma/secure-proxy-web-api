using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using MoD.SecureProxy.Data.Entities;

namespace MoD.SecureProxy.Services;

public sealed class HttpForwarder
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE"
    };

    private static readonly HashSet<string> NeverForwardHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Content-Length",
        "Content-Type", "Content-Encoding", "Content-Range", "Content-MD5"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly long _maxResponseBodyBytes;
    private readonly ILogger<HttpForwarder> _logger;

    public HttpForwarder(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HttpForwarder> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _maxResponseBodyBytes = configuration.GetValue<long>("ProxyLimits:MaxResponseBodyBytes", 2_000_000);

        if (_maxResponseBodyBytes <= 0)
        {
            throw new InvalidOperationException("ProxyLimits:MaxResponseBodyBytes must be positive.");
        }
    }

    public async Task<HttpForwardResult> ForwardAsync(
        Operation operation,
        byte[] requestBody,
        IHeaderDictionary incomingHeaders,
        CancellationToken callerCancellationToken)
    {
        if (!TryBuildTargetUri(operation, out var targetUri) ||
            !SupportedMethods.Contains(operation.HttpMethod) ||
            operation.Destination.RequestTimeoutMilliseconds <= 0)
        {
            return HttpForwardResult.Failure(503, "ForwardingConfigurationUnavailable");
        }

        var allowedHeaders = operation.HeaderRules
            .Select(rule => rule.HeaderName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var request = new HttpRequestMessage(new HttpMethod(operation.HttpMethod), targetUri)
        {
            Content = new ByteArrayContent(requestBody)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        ApplyAllowedHeaders(request, incomingHeaders, allowedHeaders);

        using var timeoutSource = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(operation.Destination.RequestTimeoutMilliseconds));
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            timeoutSource.Token);

        try
        {
            var client = _httpClientFactory.CreateClient("SecureProxyUpstream");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linkedSource.Token);

            var boundedBody = await ReadResponseBodyAsync(response, linkedSource.Token);
            if (boundedBody.TooLarge)
            {
                return HttpForwardResult.Failure(
                    StatusCodes.Status502BadGateway,
                    "UpstreamResponseTooLarge",
                    (int)response.StatusCode,
                    forwardingOccurred: true);
            }

            var contentType = response.Content.Headers.ContentType?.ToString();
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "application/octet-stream";
            }

            return HttpForwardResult.UpstreamResponse(
                (int)response.StatusCode,
                contentType,
                boundedBody.Body ?? [],
                forwardingOccurred: true);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return HttpForwardResult.Failure(
                StatusCodes.Status504GatewayTimeout,
                "UpstreamTimeout",
                forwardingOccurred: true);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            _logger.LogWarning(
                "Upstream connection failed. ExceptionType: {ExceptionType}",
                exception.GetType().Name);

            return HttpForwardResult.Failure(
                StatusCodes.Status502BadGateway,
                "UpstreamConnectionFailure",
                forwardingOccurred: true);
        }
    }

    private static bool TryBuildTargetUri(Operation operation, out Uri targetUri)
    {
        targetUri = null!;
        var baseUrl = operation.Destination.BaseUrl;
        var relativePath = operation.RelativePath;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment) ||
            relativePath.Contains('\\') || relativePath.Contains('?') || relativePath.Contains('#') ||
            relativePath.StartsWith("//", StringComparison.Ordinal) ||
            Uri.TryCreate(relativePath, UriKind.Absolute, out _))
        {
            return false;
        }

        if (relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."))
        {
            return false;
        }

        var normalizedBase = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/";
        var normalizedPath = relativePath.TrimStart('/');
        if (!Uri.TryCreate(new Uri(normalizedBase, UriKind.Absolute), normalizedPath, out targetUri))
        {
            return false;
        }

        return targetUri.Scheme == baseUri.Scheme &&
            string.Equals(targetUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) &&
            targetUri.Port == baseUri.Port;
    }

    private static void ApplyAllowedHeaders(
        HttpRequestMessage request,
        IHeaderDictionary incomingHeaders,
        HashSet<string> allowedHeaders)
    {
        var connectionNominatedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (incomingHeaders.TryGetValue("Connection", out var connectionValues))
        {
            foreach (var value in connectionValues)
            {
                if (value is null)
                {
                    continue;
                }

                foreach (var name in value.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    connectionNominatedHeaders.Add(name);
                }
            }
        }

        foreach (var header in incomingHeaders)
        {
            if (!allowedHeaders.Contains(header.Key) ||
                NeverForwardHeaders.Contains(header.Key) ||
                connectionNominatedHeaders.Contains(header.Key))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    private async Task<(byte[]? Body, bool TooLarge)> ReadResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long contentLength &&
            contentLength > _maxResponseBodyBytes)
        {
            return (null, true);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];

        while (true)
        {
            var remainingPlusOne = _maxResponseBodyBytes - output.Length + 1;
            var bytesToRead = (int)Math.Min(buffer.Length, remainingPlusOne);
            var bytesRead = await responseStream.ReadAsync(
                buffer.AsMemory(0, bytesToRead),
                cancellationToken);

            if (bytesRead == 0)
            {
                return (output.ToArray(), false);
            }

            if (output.Length + bytesRead > _maxResponseBodyBytes)
            {
                return (null, true);
            }

            output.Write(buffer, 0, bytesRead);
        }
    }
}

public sealed record HttpForwardResult(
    int ProxyStatusCode,
    string? ContentType,
    byte[]? Body,
    int? UpstreamStatusCode,
    string? FailureReason,
    bool ForwardingOccurred)
{
    public bool HasUpstreamResponse => UpstreamStatusCode.HasValue && FailureReason is null;

    public static HttpForwardResult UpstreamResponse(
        int statusCode,
        string contentType,
        byte[] body,
        bool forwardingOccurred) =>
        new(statusCode, contentType, body, statusCode, null, forwardingOccurred);

    public static HttpForwardResult Failure(
        int proxyStatusCode,
        string failureReason,
        int? upstreamStatusCode = null,
        bool forwardingOccurred = false) =>
        new(proxyStatusCode, null, null, upstreamStatusCode, failureReason, forwardingOccurred);
}
