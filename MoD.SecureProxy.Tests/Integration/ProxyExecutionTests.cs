using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MoD.SecureProxy.Tests.TestInfrastructure;
using Xunit;

namespace MoD.SecureProxy.Tests.Integration;

public sealed class ProxyExecutionTests
{
    [Fact]
    public async Task ValidConfiguredOperationIsForwardedToConfiguredDestination()
    {
        using var host = await ProxyApiTestHost.CreateAsync();
        host.Upstream.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("upstream accepted")
        });

        using var response = await ExecuteAsync(host.Client, "customer.get", """
            {"customerId":123}
            """);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("upstream accepted", await response.Content.ReadAsStringAsync());

        var sent = Assert.Single(host.Upstream.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("https://trusted-upstream.example.test/v1/customers", sent.Url);
        Assert.Contains("customerId", sent.Body, StringComparison.Ordinal);

        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("Forwarded", log.Outcome);
        Assert.True(log.ForwardingOccurred);
        Assert.Equal((int)HttpStatusCode.Accepted, log.UpstreamStatusCode);
    }

    [Fact]
    public async Task UnknownOperationIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync();

        using var response = await ExecuteAsync(host.Client, "unknown.operation", "{}", "123");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("Rejected", log.Outcome);
        Assert.False(log.ForwardingOccurred);
    }

    [Fact]
    public async Task DisabledOperationIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions { OperationActive = false });

        using var response = await ExecuteAsync(host.Client, "customer.get", """
            {"customerId":123}
            """);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        Assert.False(Assert.Single(await host.ReadRequestLogsAsync()).ForwardingOccurred);
    }

    [Fact]
    public async Task DisabledDestinationIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions { DestinationActive = false });

        using var response = await ExecuteAsync(host.Client, "customer.get", """
            {"customerId":123}
            """);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        Assert.False(Assert.Single(await host.ReadRequestLogsAsync()).ForwardingOccurred);
    }

    [Fact]
    public async Task MissingRequiredParameterIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync();

        using var response = await ExecuteAsync(host.Client, "customer.get", "{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        Assert.False(Assert.Single(await host.ReadRequestLogsAsync()).ForwardingOccurred);
    }

    [Fact]
    public async Task UnknownParameterIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync();

        using var response = await ExecuteAsync(host.Client, "customer.get", """
            {"customerId":123,"admin":true}
            """);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
    }

    [Fact]
    public async Task CallerCannotOverrideConfiguredUrlAndOnlyAllowlistedHeadersAreForwarded()
    {
        using var host = await ProxyApiTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/proxy/execute")
        {
            Content = new StringContent(
                """{"clientId":"customer.get","urlId":"123","destinationUrl":"https://attacker.example","json":{"customerId":123}}""",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("X-Allowed", "allowed-value");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "do-not-forward");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(host.Upstream.Requests);
        Assert.Equal("https://trusted-upstream.example.test/v1/customers", sent.Url);
        Assert.Equal(new[] { "allowed-value" }, sent.Headers["X-Allowed"]);
        Assert.DoesNotContain("Authorization", sent.Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidAesGcmAuthenticationTagIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions { RequiresEncryption = true });
        var invalidEnvelope = JsonSerializer.Serialize(new
        {
            nonce = Convert.ToBase64String(new byte[12]),
            ciphertext = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")),
            tag = Convert.ToBase64String(new byte[16])
        });

        using var response = await ExecuteAsync(host.Client, "customer.get", invalidEnvelope);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        Assert.Equal("Rejected", Assert.Single(await host.ReadRequestLogsAsync()).Outcome);
    }

    [Fact]
    public async Task RequestExceedingConfiguredLimitIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            MaximumRequestSizeBytes = 100
        });
        var longRequest = JsonSerializer.Serialize(new
        {
            clientId = "customer.get",
            urlId = "123",
            json = new { customerId = 123, padding = new string('a', 200) }
        });

        using var response = await host.Client.PostAsync(
            "/api/proxy/execute",
            new StringContent(longRequest, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
    }

    [Fact]
    public async Task InvalidConfiguredHttpMethodIsRejectedWithoutSendingUpstreamRequest()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions { HttpMethod = "TRACE" });

        using var response = await ExecuteAsync(host.Client, "customer.get", """
            {"customerId":123}
            """);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
    }

    [Fact]
    public async Task UpstreamHttpErrorStatusAndBodyArePreserved()
    {
        using var host = await ProxyApiTestHost.CreateAsync();
        host.Upstream.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream error")
        });

        using var response = await ExecuteAsync(host.Client, "customer.get", """
            {"customerId":123}
            """);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("upstream error", await response.Content.ReadAsStringAsync());
        Assert.Single(host.Upstream.Requests);
        Assert.Equal((int)HttpStatusCode.BadGateway, Assert.Single(await host.ReadRequestLogsAsync()).UpstreamStatusCode);
    }

    [Fact]
    public async Task UpstreamTimeoutReturnsGatewayTimeout()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions { TimeoutMilliseconds = 100 });
        host.Upstream.Responder = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        using var response = await ExecuteAsync(host.Client, "customer.get", """
            {"customerId":123}
            """);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Single(host.Upstream.Requests);
        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("UpstreamTimeout", log.FailureReason);
        Assert.True(log.ForwardingOccurred);
    }

    private static Task<HttpResponseMessage> ExecuteAsync(
        HttpClient client,
        string operationKey,
        string json,
        string urlId = "123") =>
        client.PostAsJsonAsync("/api/proxy/execute", new
        {
            clientId = operationKey,
            urlId,
            json = JsonSerializer.Deserialize<JsonElement>(json)
        });
}
