# Design Notes

## Architecture

- One ASP.NET Core API project, with focused folders for controllers, data/entities, middleware, security, and services.
- SQL Server and EF Core store destination/operation configuration, parameter and header rules, request audit, and configuration-change history.
- EF Core `DbContext` is the data access boundary. No generic repository abstraction is used because the current reads and writes are simple and already have focused service owners.
- `ProxyController` coordinates the request. `OperationLookup`, `RequestValidator`, `AesGcmPayloadDecryptor`, `HttpForwarder`, and `RequestAuditor` each own a narrow responsibility.
- There is no frontend, token issuance, authentication endpoint, JWT parsing middleware, automatic retry, or cache. An administration HTTP API is outside the current scope because no authorization boundary was specified.

## Request-contract assumptions

The assessment's request example is malformed, while its processing text says that `json` must contain `urlId`. The implemented assumption is:

1. The envelope has `clientId` and `json` properties.
2. `json` is a JSON object and must contain `urlId`.
3. `urlId` is a required `Path` parameter rule. It fills only a `{urlId}` placeholder in the trusted operation `RelativePath` from SQL Server.
4. Other path placeholders and query parameters also require explicit rules. Request parameters are separated by configured location; path/query values are not copied into the body.
5. Caller values cannot select or override the configured base URL, HTTP method, or route template. Path values reject slash, backslash, percent-encoding, dot-segment, control, query, and fragment characters; query names/values are URI-escaped.

This interpretation is documented as an assumption, not as an established meaning of the malformed sample. The encrypted format is also an assumption: `json` contains Base64 `nonce`, `ciphertext`, and `tag`, with a 12-byte nonce, 16-byte tag, and empty associated data. Keys are resolved by a database key-reference through configuration, not stored in SQL Server.

## Security and operational decisions

- The proxy fails closed before upstream forwarding for unknown/disabled operations, inactive destinations, invalid rules or values, missing required parameters, disallowed unknown parameters, malformed encrypted payloads, invalid AES-GCM tags, unsupported content types/methods, and oversized requests.
- Each request is correlated with `X-Correlation-Id`. Error responses are consistent and non-sensitive. Logs and audit records omit request/response payloads, keys, and credentials.
- Request and response size bounds are enforced. Upstream response reading is streamed into a bounded buffer. `IHttpClientFactory` manages clients, redirects are disabled, the configured timeout applies to send/read, and caller cancellation remains distinct from upstream timeout. No automatic retry is used because retry safety depends on operation semantics.
- Initial audit persistence is mandatory and fails closed. Audit completion is best-effort after a request has been forwarded; failures are logged for operations monitoring and do not replace the received upstream result.
- Mutable operation updates compare the supplied SQL Server `rowversion`, also use EF Core's concurrency predicate at save time, and record the allowed non-secret before/after fields in the same save. The update service is not exposed over HTTP.
- No cache is used, so configuration changes are observed on subsequent database lookups without invalidation semantics.

## Verification status and limits

The automated suite uses a recording mock upstream and EF Core InMemory. Rejected-request tests assert the upstream received no request. Unit tests cover update validation, stale-version preconditions, and history recording. InMemory does not emulate SQL Server changing a `rowversion` during a genuine concurrent write; the SQL Server race itself must be verified against LocalDB or an approved SQL Server. Applying migrations and inspecting SQL Server schema are separate local database checks.

## Remaining assessment clarification

The supplied partially implemented starter repository was not present in the provided project. The task contact clarified that work should start from the clean project and that the starter-repository section should be ignored. This project therefore reports no inherited defects as fixed. The exact request/encryption examples remain documented assumptions.
