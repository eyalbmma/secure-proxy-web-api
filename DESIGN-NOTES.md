# Design Notes — Secure Proxy API

## Current baseline

The supplied ZIP contains a new ASP.NET Core Web API scaffold. It does not contain a partially implemented proxy, existing database layer, or automated tests. The assessment's supplied-starter-repository requirement therefore remains unresolved; no existing defects are claimed as fixed.

## Initial architecture

- Keep one ASP.NET Core API project and separate responsibilities into focused folders.
- Use EF Core with SQL Server for proxy configuration and execution audit records.
- Keep EF Core's `DbContext` as the data access boundary for now. A separate generic repository abstraction is not justified for this initial scope.
- Use SQL Server `rowversion` on mutable configuration entities. It detects conflicting writes during configuration updates; it does not make ordinary reads detect concurrent changes.
- Store only encryption key references in the database, never key material.

## Request contract assumptions

The assessment requires a `urlId` field, but its sample request is malformed and does not establish the field's exact meaning. A tentative request shape is `clientId`, required `urlId`, and operation-specific `json`. The meaning and use of `urlId` must be confirmed or documented as an explicit assumption before request forwarding is implemented.

The API scaffold now models `clientId` and `urlId` as required strings and accepts `json` as a non-null JSON value. These types are provisional assumptions, not a claim that the malformed sample defines their exact types or semantics.

Callers will not choose the destination URL, HTTP method, or destination path. Those values must come from trusted server-side configuration.

## Decisions still to implement

- Persist audit metadata without credentials or payload contents. Initial audit-write failure blocks execution with `503`; completion writes are best-effort and logged if they fail.
- Trace configuration changes without recording secrets.
- Add automated tests with a recording mock upstream and prove rejected requests were not sent.
- Complete configuration update handling and prove SQL Server `rowversion` conflicts return `409`.
- Support path and query parameter rules after the `urlId` contract is clarified; these currently fail closed.

## Implemented in the current increment

- `POST /api/proxy/execute` is scaffolded and returns `501 Not Implemented`; it does not claim forwarding succeeded.
- Each request receives a server-generated correlation ID in `X-Correlation-Id`.
- Invalid request bodies and unhandled exceptions use safe error responses that include the correlation ID.
- Caller cancellation is logged without attempting to send a normal HTTP error response.
- Operations are looked up by the caller's `clientId` against trusted database configuration. Unknown or disabled operations and unavailable destinations are rejected before any forwarding.
- An active operation currently returns `501 Not Implemented`; no upstream request is sent in this increment.
- Validly bound execution requests must first create an audit record; if that initial write fails, execution stops with `503`. Completing an audit record is best-effort and failures are logged without secrets. Framework-rejected malformed bodies are not yet persisted to `RequestLogs` and must be covered before final delivery.
- Body parameter rules support basic JSON types, required values, string maximum length, numeric ranges, and per-operation unknown-parameter policy. Path/query rules are rejected safely until the `urlId` contract is resolved.
- Encrypted operations use an explicit provisional envelope: `json` contains Base64 `nonce`, `ciphertext`, and `tag` fields. The nonce is 12 bytes, the authentication tag is 16 bytes, and associated data is empty. Confirm this format against the assessment before treating it as final.
- AES-GCM authentication and plaintext JSON parsing happen before parameter validation. Invalid envelopes or authentication tags are rejected with `400`; missing/invalid key configuration is rejected with `503`. Key bytes are read by reference from configuration and cleared from memory after decryption. No key or decrypted payload is logged.
- The local key source reads Base64 key values from `EncryptionKeys:<key-reference>` configuration (User Secrets or environment variables), never from the database or committed settings.
## Implemented in the forwarding increment

- Valid configured operations are sent through `IHttpClientFactory`; the destination base URL, HTTP method, and relative path come only from database configuration.
- Only explicitly allowlisted inbound headers are copied. Hop-by-hop headers, `Host`, and headers nominated by `Connection` are always excluded. Redirect following is disabled and no automatic retries are configured.
- The configured destination timeout is applied to sending and reading the upstream response. Caller cancellation is rethrown and logged separately; an upstream timeout maps to `504`.
- The incoming request is bounded by a global Kestrel limit and the operation's configured request size. The response is read as a stream with a configured maximum and a bounded buffer; oversized upstream responses map to `502`.
- Upstream status codes and bodies are returned after a response is actually received. The audit record stores the upstream status and whether forwarding was attempted; payloads and headers are not logged.
- `urlId` remains a required top-level request field under the provisional DTO assumption. Its meaning is unresolved, so it is not used to alter the destination URL, method, or path. The encrypted envelope shape is also still provisional.

This document records the first implementation increment and will be updated as those decisions are implemented.
