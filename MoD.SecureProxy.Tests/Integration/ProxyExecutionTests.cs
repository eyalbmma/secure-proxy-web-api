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
        Assert.Equal("https://trusted-upstream.example.test/v1/customers/123", sent.Url);
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

        using var response = await ExecuteAsync(host.Client, "unknown.operation", "{}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("Rejected", log.Outcome);
        Assert.False(log.ForwardingOccurred);
    }

    [Fact]
    public async Task MalformedJsonIsAuditedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync();

        using var response = await host.Client.PostAsync(
            "/api/proxy/execute",
            new StringContent("{", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("invalid_request", log.OperationKey);
        Assert.Equal("Rejected", log.Outcome);
        Assert.Equal("InvalidRequestModel", log.FailureReason);
        Assert.False(log.ForwardingOccurred);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_request", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MissingRequiredEnvelopeFieldIsAuditedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync();
        using var response = await host.Client.PostAsJsonAsync("/api/proxy/execute", new
        {
            json = new { customerId = 123, urlId = "123" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("invalid_request", log.OperationKey);
        Assert.Equal("Rejected", log.Outcome);
        Assert.Equal("InvalidRequestModel", log.FailureReason);
        Assert.False(log.ForwardingOccurred);
    }

    [Fact]
    public async Task JsonMissingRequiredUrlIdIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/proxy/execute", new
        {
            clientId = "customer.get",
            json = new { customerId = 123 }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("Rejected", log.Outcome);
        Assert.Equal("required_parameter_missing", log.FailureReason);
        Assert.False(log.ForwardingOccurred);
    }

    [Fact]
    public async Task InvalidModelWithUnavailableAuditStoreFailsClosed()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            AuditFailureMode = AuditFailureMode.FailInitialWrite
        });

        using var response = await host.Client.PostAsync(
            "/api/proxy/execute",
            new StringContent("{", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        Assert.Empty(await host.ReadRequestLogsAsync());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("audit_unavailable", body.RootElement.GetProperty("code").GetString());
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
                """{"clientId":"customer.get","destinationUrl":"https://attacker.example","json":{"customerId":123,"urlId":"123"}}""",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("X-Allowed", "allowed-value");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "do-not-forward");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(host.Upstream.Requests);
        Assert.Equal("https://trusted-upstream.example.test/v1/customers/123", sent.Url);
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
    public async Task ValidAesGcmPayloadIsDecryptedValidatedAndForwarded()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            RequiresEncryption = true
        });

        var key = new byte[32];
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var plaintext = Encoding.UTF8.GetBytes("{\"customerId\":123,\"urlId\":\"123\"}");
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Array.Empty<byte>());
        }

        using var response = await host.Client.PostAsJsonAsync("/api/proxy/execute", new
        {
            clientId = "customer.get",
            json = new
            {
                nonce = Convert.ToBase64String(nonce),
                ciphertext = Convert.ToBase64String(ciphertext),
                tag = Convert.ToBase64String(tag)
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(host.Upstream.Requests);
        Assert.Equal("https://trusted-upstream.example.test/v1/customers/123", sent.Url);
        Assert.Equal("{\"customerId\":123}", sent.Body);
        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("Forwarded", log.Outcome);
        Assert.True(log.ForwardingOccurred);
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

    [Fact]
    public async Task InitialAuditFailureFailsClosedBeforeCallingUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            AuditFailureMode = AuditFailureMode.FailInitialWrite
        });

        using var response = await ExecuteAsync(host.Client, "customer.get", """
            {"customerId":123}
            """);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        Assert.Empty(await host.ReadRequestLogsAsync());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("audit_unavailable", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CompletionAuditFailureDoesNotReplaceAReceivedUpstreamResponse()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            AuditFailureMode = AuditFailureMode.FailCompletionWrite
        });
        host.Upstream.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("accepted")
        });

        using var response = await ExecuteAsync(host.Client, "customer.get", """
            {"customerId":123}
            """);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("accepted", await response.Content.ReadAsStringAsync());
        Assert.Single(host.Upstream.Requests);

        var persistedLog = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("Pending", persistedLog.Outcome);
        Assert.False(persistedLog.ForwardingOccurred);
    }

    [Fact]
    public async Task UpstreamResponseOverConfiguredLimitIsRejectedAndAudited()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            MaximumResponseSizeBytes = 8
        });
        host.Upstream.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("response is too large")
        });

        using var response = await ExecuteAsync(host.Client, "customer.get", """
            {"customerId":123}
            """);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Single(host.Upstream.Requests);
        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("UpstreamResponseTooLarge", log.FailureReason);
        Assert.True(log.ForwardingOccurred);
    }

    [Fact]
    public async Task MalformedEncryptedPayloadIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync(
            new ProxySeedOptions { RequiresEncryption = true });

        const string malformedEnvelope =
            """{"nonce":"not-base64","ciphertext":"e30=","tag":"AAAAAAAAAAAAAAAAAAAAAA=="}""";

        using var response = await ExecuteAsync(
            host.Client,
            "customer.get",
            malformedEnvelope);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);

        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("Rejected", log.Outcome);
        Assert.Equal("InvalidEncryptedPayload", log.FailureReason);
        Assert.False(log.ForwardingOccurred);
    }
    [Fact]
    public async Task UnsupportedContentTypeIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync();

        using var content = new StringContent(
            """{"clientId":"customer.get","urlId":"123","json":{"customerId":123}}""",
            Encoding.UTF8,
            "text/plain");

        using var response = await host.Client.PostAsync(
            "/api/proxy/execute",
            content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
    }




    [Fact]
    public async Task CallerCancellationIsNotReportedAsAnUpstreamTimeout()
    {
        using var host = await ProxyApiTestHost.CreateAsync();
        host.Upstream.Responder = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        using var cancellationSource = new CancellationTokenSource();
        var requestTask = ExecuteAsync(
            host.Client,
            "customer.get",
            """{"customerId":123}""",
            cancellationToken: cancellationSource.Token);

        await host.Upstream.WaitForRequestAsync.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await requestTask);
        var log = await WaitForAuditOutcomeAsync(host, "Cancelled");
        Assert.Equal("Cancelled", log.Outcome);
        Assert.Equal("CallerCancelled", log.FailureReason);
        Assert.True(log.ForwardingOccurred);
        Assert.Single(host.Upstream.Requests);
    }
    [Fact]
    public async Task JsonUrlIdIsAppliedOnlyToConfiguredPathTemplate()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            IncludeRequiredCustomerIdRule = false,
            IncludeRequiredUrlIdPathRule = true,
            RelativePath = "v1/customers/{urlId}"
        });

        host.Upstream.Responder = (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));

        using var content = new StringContent(
            """{"clientId":"customer.get","json":{"urlId":"customer-123"}}""",
            Encoding.UTF8,
            "application/json");

        using var response = await host.Client.PostAsync(
            "/api/proxy/execute",
            content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var sent = Assert.Single(host.Upstream.Requests);
        Assert.Equal(
            "https://trusted-upstream.example.test/v1/customers/customer-123",
            sent.Url);
        Assert.Equal("{}", sent.Body);
    }

    [Fact]
    public async Task JsonUrlIdIsEscapedAsOneConfiguredPathSegment()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            IncludeRequiredCustomerIdRule = false,
            IncludeRequiredUrlIdPathRule = true,
            RelativePath = "v1/customers/{urlId}"
        });

        using var response = await host.Client.PostAsJsonAsync("/api/proxy/execute", new
        {
            clientId = "customer.get",
            json = new { urlId = "customer 123" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(host.Upstream.Requests);
        Assert.Equal("https://trusted-upstream.example.test/v1/customers/customer%20123", sent.Url);
    }

    [Theory]
    [InlineData("../admin")]
    [InlineData("%2e%2e%2fadmin")]
    [InlineData("folder\\..\\admin")]
    public async Task UnsafeJsonUrlIdIsRejectedAndNeverReachesUpstream(string urlId)
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            IncludeRequiredCustomerIdRule = false,
            IncludeRequiredUrlIdPathRule = true,
            RelativePath = "v1/customers/{urlId}"
        });

        using var response = await host.Client.PostAsJsonAsync("/api/proxy/execute", new
        {
            clientId = "customer.get",
            json = new { urlId }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("Rejected", log.Outcome);
        Assert.False(log.ForwardingOccurred);
    }

    [Fact]
    public async Task QueryRuleIsEncodedAndRemovedFromForwardedBody()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            IncludeRequiredCustomerIdRule = true,
            IncludeOptionalSearchQueryRule = true
        });

        using var response = await host.Client.PostAsJsonAsync("/api/proxy/execute", new
        {
            clientId = "customer.get",
            json = new { customerId = 123, urlId = "123", search = "a&admin=true" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(host.Upstream.Requests);
        Assert.Equal("https://trusted-upstream.example.test/v1/customers/123?search=a%26admin%3Dtrue", sent.Url);
        Assert.Equal("{\"customerId\":123}", sent.Body);
    }

    [Fact]
    public async Task InvalidQueryValueIsRejectedAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            IncludeOptionalSearchQueryRule = true
        });

        using var response = await host.Client.PostAsJsonAsync("/api/proxy/execute", new
        {
            clientId = "customer.get",
            json = new { customerId = 123, urlId = "123", search = new string('x', 101) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
    }

    [Fact]
    public async Task UnconfiguredPathPlaceholderIsConfigurationFailureAndNeverReachesUpstream()
    {
        using var host = await ProxyApiTestHost.CreateAsync(new ProxySeedOptions
        {
            IncludeRequiredUrlIdPathRule = false,
            RelativePath = "v1/customers/{unconfigured}"
        });

        using var response = await ExecuteAsync(host.Client, "customer.get", """{"customerId":123}""");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(host.Upstream.Requests);
        var log = Assert.Single(await host.ReadRequestLogsAsync());
        Assert.Equal("Failed", log.Outcome);
        Assert.False(log.ForwardingOccurred);
    }
    private static Task<HttpResponseMessage> ExecuteAsync(
        HttpClient client,
        string operationKey,
        string json,
        CancellationToken cancellationToken = default) =>
        client.PostAsJsonAsync("/api/proxy/execute", new
        {
            clientId = operationKey,
            json = AddDefaultUrlId(JsonSerializer.Deserialize<JsonElement>(json))
        }, cancellationToken);

    private static JsonElement AddDefaultUrlId(JsonElement json)
    {
        var values = json.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        if (!values.ContainsKey("urlId"))
        {
            using var urlId = JsonDocument.Parse("\"123\"");
            values["urlId"] = urlId.RootElement.Clone();
        }

        return JsonSerializer.SerializeToElement(values);
    }

    private static async Task<MoD.SecureProxy.Data.Entities.RequestLog> WaitForAuditOutcomeAsync(
        ProxyApiTestHost host,
        string expectedOutcome)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var logs = await host.ReadRequestLogsAsync();
            if (logs.Count == 1 && logs[0].Outcome == expectedOutcome)
            {
                return logs[0];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        return Assert.Single(await host.ReadRequestLogsAsync());
    }
}
