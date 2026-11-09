# MoD Secure Proxy Web API

Backend-only ASP.NET Core Web API targeting .NET 8. It accepts an operation key and JSON parameters, validates them against server-side configuration, and forwards valid requests to the configured upstream destination.

## Prerequisites

- Windows 10/11.
- .NET 8 SDK. Verify with `dotnet --list-sdks`.
- SQL Server Express LocalDB. This project was developed against SQL Server 2019 LocalDB.
- Visual Studio 2022 or later is optional for running the API, but its Package Manager Console is used in the database steps below.

If LocalDB is not installed, install **SQL Server Express LocalDB** through Visual Studio Installer: select **Modify**, then add the LocalDB individual component or a data-development workload. SQL Server Express also provides a standalone LocalDB installer. See Microsoft's [LocalDB installation documentation](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb).

## Database setup (LocalDB)

The development connection string in `MoD.SecureProxy/MoD.SecureProxy/appsettings.json` uses the LocalDB instance `MoDSecureProxy` and database `MoDSecureProxy`:

```text
Server=(localdb)\MoDSecureProxy;Database=MoDSecureProxy;Trusted_Connection=True;...
```

Open PowerShell and check that LocalDB and the instance exist:

```powershell
sqllocaldb versions
sqllocaldb info
sqllocaldb info MoDSecureProxy
```

If `MoDSecureProxy` does not exist, create it. If it exists but is stopped, start it:

```powershell
sqllocaldb create MoDSecureProxy 15.0
sqllocaldb start MoDSecureProxy
```

Do not run `create` again if the instance already exists. In Visual Studio, connect Server/Object Explorer to `(localdb)\MoDSecureProxy` if you want to inspect the database.

### Apply the existing migrations

The EF Core migration files are included in the repository; you do not need to create a new migration for a clean setup. Open `MoD.SecureProxy/MoD.SecureProxy.sln` in Visual Studio, then open **Tools → NuGet Package Manager → Package Manager Console**. Set **Default project** to `MoD.SecureProxy` and run:

```powershell
Update-Database
```

This creates the database and applies all migrations. Confirm completion in the console or inspect `__EFMigrationsHistory` in SQL Server Object Explorer. Run `Add-Migration` only after changing the EF entity/model and intentionally creating a new schema migration.

### Add the optional demo operation

After applying migrations, connect a query window to `(localdb)\MoDSecureProxy`, open [`sql/seed-demo-configuration.sql`](sql/seed-demo-configuration.sql), and execute it. It adds or updates the named local demo operation and its parameter rules. The destination and operation remain **inactive**, and the destination URL is a reserved placeholder. The endpoint will reject this demo operation until you replace the URL with an approved test upstream and deliberately enable both rows. Do not enable or call a real upstream without authorization.

For another SQL Server, override `ConnectionStrings:ProxyDb` using the environment variable `ConnectionStrings__ProxyDb` or another secure configuration source. Do not commit database credentials.

## Encryption key configuration

Encryption keys are optional for the inactive demo operation. If you configure an encrypted operation, store the key outside SQL Server and source control. The key source reads Base64 values from `EncryptionKeys:<key-reference>`; use Visual Studio User Secrets or a secure environment variable such as `EncryptionKeys__<key-reference>`. Accepted AES key sizes are 16, 24, or 32 bytes. Never put a real key in `appsettings.json` or Git.

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

The assessment says `json` must contain `urlId`, but its sample request is malformed. This implementation assumes `json.urlId` is a required path parameter and can fill only a matching placeholder in the configured operation's `RelativePath`, such as `v1/customers/{urlId}`. Values cannot select or override the destination host, HTTP method, or configured route. Path and query values are validated and escaped; path traversal is rejected. Body, path, and query rules are configured separately in the database.

For encrypted operations, this implementation assumes `json` is an envelope with Base64 `nonce`, `ciphertext`, and `tag` properties, using a 12-byte nonce, 16-byte AES-GCM tag, and empty associated data. Invalid encoding, authentication, or decrypted JSON is rejected before forwarding. These wire-format details are implementation assumptions because the assessment's sample is malformed.

## Run and test

Open `MoD.SecureProxy/MoD.SecureProxy.sln`, set `MoD.SecureProxy` as the startup project, and run with the HTTPS profile. Swagger is enabled in Development. The application requires the configured SQL Server connection at startup.

From the repository root, run the automated test suite:

```powershell
dotnet test .\MoD.SecureProxy\MoD.SecureProxy.sln
```

The xUnit tests use ASP.NET Core `WebApplicationFactory`, EF Core InMemory, and a recording mock upstream; they do not require SQL Server. The suite covers forwarding, policy and parameter rejection, path/query construction, path traversal and query injection prevention, encryption failures, header filtering, request/response limits, timeouts, caller cancellation, and audit-storage behavior. Rejected-request tests verify that the mock upstream received no request.

## Operational behavior and scope

The API uses focused services and EF Core `DbContext` directly; a generic repository layer is not added. There is no configuration cache, automatic retry, rate limiter, concurrency cap, or circuit breaker in this assessment implementation. The design notes explain the retry/idempotency and resilience decisions. Operation updates use optimistic rowversion checks and record non-secret history through an internal service; no HTTP administration endpoint is exposed because no authorization boundary was specified.

Audit creation must succeed before forwarding; if SQL Server is unavailable at request start, the request fails closed with `503`. Completion audit writes are best-effort so they do not replace a response already received from upstream; failures are logged. Request and response payloads, keys, and credentials are not written to audit records.

The task contact clarified that work should start from the clean project and that the missing starter-repository section should be ignored. The available project therefore contains no claimed fixes to inherited defects; see [`DEFECT-REPORT.md`](DEFECT-REPORT.md).
