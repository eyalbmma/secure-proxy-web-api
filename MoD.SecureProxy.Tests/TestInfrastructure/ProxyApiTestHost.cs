using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MoD.SecureProxy.Data;
using MoD.SecureProxy.Data.Entities;

namespace MoD.SecureProxy.Tests.TestInfrastructure;

public sealed record ProxySeedOptions
{
    public bool OperationActive { get; init; } = true;
    public bool DestinationActive { get; init; } = true;
    public bool RequiresEncryption { get; init; }
    public bool IncludeRequiredCustomerIdRule { get; init; } = true;
    public string HttpMethod { get; init; } = "POST";
    public int TimeoutMilliseconds { get; init; } = 2_000;
    public long MaximumRequestSizeBytes { get; init; } = 200_000;
}

public sealed class ProxyApiTestHost : IDisposable
{
    private const string DatabaseNamePrefix = "SecureProxyTests_";
    private readonly TestWebApplicationFactory _factory;

    private ProxyApiTestHost(
        TestWebApplicationFactory factory,
        HttpClient client,
        RecordingHttpMessageHandler upstream)
    {
        _factory = factory;
        Client = client;
        Upstream = upstream;
    }

    public HttpClient Client { get; }
    public RecordingHttpMessageHandler Upstream { get; }

    public static async Task<ProxyApiTestHost> CreateAsync(ProxySeedOptions? options = null)
    {
        options ??= new ProxySeedOptions();
        var databaseName = DatabaseNamePrefix + Guid.NewGuid().ToString("N");
        var upstream = new RecordingHttpMessageHandler();
        var factory = new TestWebApplicationFactory(databaseName, upstream);
        var client = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        await SeedAsync(dbContext, options);

        return new ProxyApiTestHost(factory, client, upstream);
    }

    public async Task<List<RequestLog>> ReadRequestLogsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        return await dbContext.RequestLogs.AsNoTracking().OrderBy(log => log.Id).ToListAsync();
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }

    private static async Task SeedAsync(ProxyDbContext dbContext, ProxySeedOptions options)
    {
        var destination = new Destination
        {
            Id = 1,
            Name = "configured-test-destination",
            BaseUrl = "https://trusted-upstream.example.test",
            IsActive = options.DestinationActive,
            RequestTimeoutMilliseconds = options.TimeoutMilliseconds,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var profile = options.RequiresEncryption
            ? new EncryptionProfile
            {
                Id = 1,
                Name = "test-encryption-profile",
                KeySourceReference = "test-key"
            }
            : null;

        var operation = new Operation
        {
            Id = 1,
            OperationKey = "customer.get",
            DestinationId = destination.Id,
            Destination = destination,
            HttpMethod = options.HttpMethod,
            RelativePath = "v1/customers",
            IsActive = options.OperationActive,
            MaxRequestSizeBytes = options.MaximumRequestSizeBytes,
            AllowUnknownParameters = false,
            RequiresEncryption = options.RequiresEncryption,
            EncryptionProfileId = profile?.Id,
            EncryptionProfile = profile
        };

        if (options.IncludeRequiredCustomerIdRule)
        {
            operation.ParameterRules.Add(new ParameterRule
            {
                Id = 1,
                OperationId = operation.Id,
                Operation = operation,
                ParameterName = "customerId",
                IsRequired = true,
                DataType = "integer",
                Location = ParameterLocation.Body
            });
        }

        operation.HeaderRules.Add(new HeaderRule
        {
            Id = 1,
            OperationId = operation.Id,
            Operation = operation,
            HeaderName = "X-Allowed"
        });

        dbContext.Destinations.Add(destination);
        if (profile is not null)
        {
            dbContext.EncryptionProfiles.Add(profile);
        }

        dbContext.Operations.Add(operation);
        await dbContext.SaveChangesAsync();
    }

    private sealed class TestWebApplicationFactory(
        string databaseName,
        RecordingHttpMessageHandler upstream) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ProxyDb"] = "Server=(localdb)\\MSSQLLocalDB;Database=NotUsedByTests",
                    ["ProxyLimits:MaxRequestBodyBytes"] = "2000000",
                    ["ProxyLimits:MaxResponseBodyBytes"] = "2000000",
                    ["EncryptionKeys:test-key"] = Convert.ToBase64String(new byte[32])
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ProxyDbContext>>();
                services.RemoveAll<ProxyDbContext>();
                services.AddDbContext<ProxyDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName));

                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new RecordingHttpClientFactory(upstream));
            });
        }
    }
}
