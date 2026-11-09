using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using MoD.SecureProxy.Data;
using MoD.SecureProxy.Middleware;
using MoD.SecureProxy.Models;
using MoD.SecureProxy.Services;
using MoD.SecureProxy.Security;

var builder = WebApplication.CreateBuilder(args);

var maxRequestBodyBytes = builder.Configuration.GetValue<long>(
    "ProxyLimits:MaxRequestBodyBytes",
    2_000_000);
if (maxRequestBodyBytes <= 0)
{
    throw new InvalidOperationException("ProxyLimits:MaxRequestBodyBytes must be positive.");
}

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maxRequestBodyBytes);

// Add services to the container.

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var correlationId = context.HttpContext.Items[CorrelationIdMiddleware.ContextItemKey] as string
                ?? context.HttpContext.TraceIdentifier;

            return new BadRequestObjectResult(new ApiErrorResponse(
                "invalid_request",
                "The request body is invalid.",
                correlationId));
        };
    });
builder.Services.AddAuthorization();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("ProxyDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'ProxyDb' is missing. Configure it with User Secrets or an environment variable.");
}

builder.Services.AddDbContext<ProxyDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<OperationLookup>();
builder.Services.AddScoped<RequestAuditor>();
builder.Services.AddScoped<RequestValidator>();
builder.Services.AddScoped<IKeySource, ConfigurationKeySource>();
builder.Services.AddScoped<AesGcmPayloadDecryptor>();
builder.Services.AddHttpClient("SecureProxyUpstream", client =>
    client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.GZip |
            System.Net.DecompressionMethods.Deflate |
            System.Net.DecompressionMethods.Brotli
    });
builder.Services.AddScoped<HttpForwarder>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
