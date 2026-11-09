# MoD Secure Proxy Web API

Backend-only ASP.NET Core Web API targeting .NET 8.

## Prerequisites

- .NET 8 SDK
- SQL Server 2019 LocalDB (or an approved SQL Server instance)
- Visual Studio 2022 or later

## Local database setup

The default development connection string points to a LocalDB instance named `MoDSecureProxy`. On a machine where LocalDB is installed, create the instance with:

```powershell
sqllocaldb create MoDSecureProxy 15.0 -s
```

In Visual Studio, open the solution and run `Update-Database` from the NuGet Package Manager Console. The EF Core migrations create the schema. The database contains no seeded destination or operation records yet; a request cannot be forwarded until trusted configuration has been added.

For a different SQL Server, override `ConnectionStrings:ProxyDb` through an environment variable named `ConnectionStrings__ProxyDb`. Do not commit credentials.

## Encryption key configuration

Encryption key values are read from configuration by key reference and must not be stored in SQL Server or committed settings. For local development, provide them through an approved secret store or environment variables such as `EncryptionKeys__<key-reference>`. Values are Base64-encoded AES keys of 16, 24, or 32 bytes.

## Run

Open `MoD.SecureProxy/MoD.SecureProxy.sln`, set `MoD.SecureProxy` as the startup project, and run with the HTTPS profile. Swagger is available in the Development environment.

The endpoint is `POST /api/proxy/execute`. The current provisional envelope is `{ "clientId": "...", "urlId": "...", "json": { ... } }`. The assessment's example is malformed, so the exact meaning of `urlId` and encrypted-payload envelope must be confirmed or documented as assumptions.

## Current implementation status

The API performs operation lookup, fail-closed checks, body parameter validation, optional AES-GCM decryption, header allowlisting, bounded request/response handling, and configured upstream forwarding. Path/query parameter rules, configuration update APIs/history, seeded sample configuration, and automated tests remain to be completed. The supplied partially implemented starter repository was not included, so no pre-existing defects are claimed as fixed.
