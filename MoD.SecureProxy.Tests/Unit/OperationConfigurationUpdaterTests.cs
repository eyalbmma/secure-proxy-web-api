using Microsoft.EntityFrameworkCore;
using MoD.SecureProxy.Data;
using MoD.SecureProxy.Data.Entities;
using MoD.SecureProxy.Services;
using Xunit;

namespace MoD.SecureProxy.Tests.Unit;

public sealed class OperationConfigurationUpdaterTests
{
    [Fact]
    public async Task UpdateWithCurrentVersionPersistsConfigurationAndReturnsUpdatedVersion()
    {
        var fixture = await ConfigurationFixture.CreateAsync();
        await using var dbContext = fixture.CreateContext();
        var updater = new OperationConfigurationUpdater(dbContext);

        var result = await updater.UpdateAsync(
            fixture.OperationId,
            fixture.RowVersion,
            new OperationConfigurationUpdate(
                IsActive: false,
                HttpMethod: "get",
                RelativePath: "v2/customers",
                MaxRequestSizeBytes: 4096,
                AllowUnknownParameters: true),
            CancellationToken.None);

        Assert.Equal(OperationConfigurationUpdateStatus.Updated, result.Status);
        Assert.NotNull(result.RowVersion);

        var updated = await dbContext.Operations.AsNoTracking()
            .SingleAsync(operation => operation.Id == fixture.OperationId);
        Assert.False(updated.IsActive);
        Assert.Equal("GET", updated.HttpMethod);
        Assert.Equal("v2/customers", updated.RelativePath);
        Assert.Equal(4096, updated.MaxRequestSizeBytes);
        Assert.True(updated.AllowUnknownParameters);
    }

    [Fact]
    public async Task StaleVersionReturnsConflictAndDoesNotChangeConfiguration()
    {
        var fixture = await ConfigurationFixture.CreateAsync();
        await using var dbContext = fixture.CreateContext();
        var updater = new OperationConfigurationUpdater(dbContext);
        var staleVersion = fixture.RowVersion.Concat(new byte[] { 1 }).ToArray();

        var result = await updater.UpdateAsync(
            fixture.OperationId,
            staleVersion,
            new OperationConfigurationUpdate(
                IsActive: false,
                HttpMethod: "GET",
                RelativePath: "v2/customers",
                MaxRequestSizeBytes: 4096,
                AllowUnknownParameters: true),
            CancellationToken.None);

        Assert.Equal(OperationConfigurationUpdateStatus.Conflict, result.Status);
        var unchanged = await dbContext.Operations.AsNoTracking()
            .SingleAsync(operation => operation.Id == fixture.OperationId);
        Assert.True(unchanged.IsActive);
        Assert.Equal("POST", unchanged.HttpMethod);
        Assert.Equal("v1/customers", unchanged.RelativePath);
    }

    [Theory]
    [InlineData("https://attacker.example/path", "invalid_relative_path")]
    [InlineData("../admin", "invalid_relative_path")]
    [InlineData("v1/customers?admin=true", "invalid_relative_path")]
    [InlineData("TRACE", "unsupported_http_method")]
    public async Task InvalidConfigurationIsRejectedWithoutChangingStoredValues(
        string invalidValue,
        string expectedError)
    {
        var fixture = await ConfigurationFixture.CreateAsync();
        await using var dbContext = fixture.CreateContext();
        var updater = new OperationConfigurationUpdater(dbContext);
        var update = expectedError == "unsupported_http_method"
            ? new OperationConfigurationUpdate(true, invalidValue, "v1/customers", 4096, false)
            : new OperationConfigurationUpdate(true, "GET", invalidValue, 4096, false);

        var result = await updater.UpdateAsync(
            fixture.OperationId,
            fixture.RowVersion,
            update,
            CancellationToken.None);

        Assert.Equal(OperationConfigurationUpdateStatus.Invalid, result.Status);
        Assert.Equal(expectedError, result.ErrorCode);
        var unchanged = await dbContext.Operations.AsNoTracking()
            .SingleAsync(operation => operation.Id == fixture.OperationId);
        Assert.Equal("POST", unchanged.HttpMethod);
        Assert.Equal("v1/customers", unchanged.RelativePath);
    }

    [Fact]
    public async Task UnknownOperationReturnsNotFound()
    {
        var fixture = await ConfigurationFixture.CreateAsync();
        await using var dbContext = fixture.CreateContext();
        var updater = new OperationConfigurationUpdater(dbContext);

        var result = await updater.UpdateAsync(
            int.MaxValue,
            fixture.RowVersion,
            new OperationConfigurationUpdate(true, "GET", "v1/customers", 4096, false),
            CancellationToken.None);

        Assert.Equal(OperationConfigurationUpdateStatus.NotFound, result.Status);
    }

    private sealed record ConfigurationFixture(string DatabaseName, int OperationId, byte[] RowVersion)
    {
        public ProxyDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ProxyDbContext>()
                .UseInMemoryDatabase(DatabaseName)
                .Options;
            return new ProxyDbContext(options);
        }

        public static async Task<ConfigurationFixture> CreateAsync()
        {
            var fixture = new ConfigurationFixture(
                "ConfigurationUpdaterTests_" + Guid.NewGuid().ToString("N"),
                OperationId: 7,
                RowVersion: []);

            await using var dbContext = fixture.CreateContext();
            var destination = new Destination
            {
                Id = 3,
                Name = "test-destination",
                BaseUrl = "https://trusted.example.test",
                IsActive = true,
                RequestTimeoutMilliseconds = 1_000,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            var operation = new Operation
            {
                Id = fixture.OperationId,
                OperationKey = "test.operation",
                DestinationId = destination.Id,
                Destination = destination,
                HttpMethod = "POST",
                RelativePath = "v1/customers",
                IsActive = true,
                MaxRequestSizeBytes = 1024
            };

            dbContext.Destinations.Add(destination);
            dbContext.Operations.Add(operation);
            await dbContext.SaveChangesAsync();

            var rowVersion = await dbContext.Operations.AsNoTracking()
                .Where(item => item.Id == fixture.OperationId)
                .Select(item => item.RowVersion)
                .SingleAsync();

            return fixture with { RowVersion = rowVersion };
        }
    }
}
