# MoD Secure Proxy Web API

Backend-only ASP.NET Core Web API targeting .NET 8. It accepts an operation key and JSON parameters, validates them against server-side configuration, and forwards valid requests to the configured upstream destination.

## Requirements

- .NET 8 SDK
- SQL Server 2019 LocalDB or an approved SQL Server instance
- Visual Studio 2022 or later (optional)

## Database setup

The development connection string targets the `MoDSecureProxy` LocalDB instance. Create it if necessary:

```powershell
sqllocaldb create MoDSecureProxy 15.0 -s
```

Open `MoD.SecureProxy/MoD.SecureProxy.sln`. The EF Core migrations are included. In Visual Studio Package Manager Console, select `MoD.SecureProxy` as the default project and apply them:

```powershell
Update-Database
```

Run [`sql/seed-demo-configuration.sql`](sql/seed-demo-configuration.sql) against the database to add the disabled `customer.get` demonstration operation. The destination address is a reserved placeholder and both destination and operation are inactive. Replace the URL with an approved test upstream before enabling them. The script contains no credentials and only updates the named inactive demo placeholder when bringing it forward to the current `urlId` path-template contract.

For another SQL Server, set `ConnectionStrings__ProxyDb` through a secure local configuration source. Do not commit credentials or encryption keys.

## Request contract and assumptions

`POST /api/proxy/execute` accepts this shape:

```json
{
  "clientId": "customer.get",
  "json": {
    "urlId": "customer-123",
    "customerId": 123
  }
}
```

The assessment says `json` must contain `urlId`, but its sample request is malformed. This implementation assumes `json.urlId` is a required path parameter and can fill only a matching placeholder in the configured operation's `RelativePath`, such as `v1/customers/{urlId}`. Values cannot select or override the destination host, HTTP method, or configured route. Path and query values are escaped and validated; path traversal is rejected. Body, path, and query rules are configured separately in the database.

For encrypted operations, this implementation assumes `json` is an envelope with Base64 `nonce`, `ciphertext`, and `tag` properties, using a 12-byte nonce, 16-byte AES-GCM tag, and empty associated data. Invalid encoding, authentication, or decrypted JSON is rejected before forwarding. Confirm these wire-format assumptions with the assessment owner if clarification is available.

## Run and test

Run the API with the HTTPS launch profile. Swagger is enabled in Development. To run the automated tests from the repository root:

```powershell
dotnet test .\MoD.SecureProxy\MoD.SecureProxy.sln
```

The xUnit tests use ASP.NET Core `WebApplicationFactory`, EF Core InMemory, and a recording mock upstream. They do not need SQL Server. The suite covers forwarding, policy and parameter rejection, path/query construction, path traversal and query injection prevention, encryption failures, header filtering, request/response limits, timeouts, caller cancellation, and audit-storage behavior. Every rejection test that exercises the proxy endpoint verifies that the mock upstream received no request.

## Design and scope

The API uses focused services and the EF Core `DbContext` directly; a generic repository layer is not added because it would not improve the current query/write boundaries. Operation configuration updates use optimistic rowversion checks and write configuration-change history in the same save. The updater is an internal service, not an exposed administration endpoint; an HTTP management API would require a separately specified authorization boundary.

Audit creation must succeed before forwarding; if SQL Server is unavailable at request start, the request fails closed with `503`. Completion audit writes are best-effort so they do not replace a response already received from upstream; the service logs an operational warning. No payloads, keys, or forwarded credentials are written to logs or audit records.

The available starter project did not include the partially implemented repository mentioned in the assessment. The task contact clarified that work should start from the clean project and that the starter-repository section should be ignored. Therefore this submission does not claim to identify or fix inherited defects; see [`DEFECT-REPORT.md`](DEFECT-REPORT.md).
