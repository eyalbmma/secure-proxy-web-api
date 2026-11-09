using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MoD.SecureProxy.Data.Entities;
using MoD.SecureProxy.Middleware;
using MoD.SecureProxy.Models;
using MoD.SecureProxy.Security;
using MoD.SecureProxy.Services;

namespace MoD.SecureProxy.Controllers;

[ApiController]
[Route("api/proxy")]
public sealed class ProxyController : ControllerBase
{
    private readonly OperationLookup _operationLookup;
    private readonly RequestAuditor _requestAuditor;
    private readonly RequestValidator _requestValidator;
    private readonly AesGcmPayloadDecryptor _payloadDecryptor;
    private readonly HttpForwarder _httpForwarder;
    private readonly ILogger<ProxyController> _logger;

    public ProxyController(
        OperationLookup operationLookup,
        RequestAuditor requestAuditor,
        RequestValidator requestValidator,
        AesGcmPayloadDecryptor payloadDecryptor,
        HttpForwarder httpForwarder,
        ILogger<ProxyController> logger)
    {
        _operationLookup = operationLookup;
        _requestAuditor = requestAuditor;
        _requestValidator = requestValidator;
        _payloadDecryptor = payloadDecryptor;
        _httpForwarder = httpForwarder;
        _logger = logger;
    }

    [HttpPost("execute")]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status501NotImplemented)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Execute(
        [FromBody] ExecuteProxyRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ContextItemKey] as string
            ?? HttpContext.TraceIdentifier;

        var auditLog = await _requestAuditor.TryStartAsync(
            correlationId,
            request.ClientId,
            cancellationToken);

        if (auditLog is null)
        {
            return Error(
                StatusCodes.Status503ServiceUnavailable,
                "audit_unavailable",
                "The request cannot be processed at this time.",
                correlationId);
        }

        byte[]? decryptedPayload = null;
        byte[]? outgoingBody = null;
        var forwardingStarted = false;
        try
        {
            var operation = await _operationLookup.FindByKeyAsync(request.ClientId, cancellationToken);
            if (operation is null)
            {
                return await CompleteWithErrorAsync(
                    auditLog, StatusCodes.Status404NotFound, "operation_not_found",
                    "The requested operation was not found.", "Rejected", "OperationNotFound", correlationId);
            }

            auditLog.DestinationId = operation.DestinationId;

            if (!operation.IsActive)
            {
                return await CompleteWithErrorAsync(
                    auditLog, StatusCodes.Status403Forbidden, "operation_disabled",
                    "The requested operation is unavailable.", "Rejected", "OperationDisabled", correlationId);
            }

            if (operation.Destination is null || !operation.Destination.IsActive)
            {
                return await CompleteWithErrorAsync(
                    auditLog, StatusCodes.Status503ServiceUnavailable, "destination_unavailable",
                    "The configured destination is unavailable.", "Rejected", "DestinationUnavailable", correlationId);
            }

            var incomingBodySize = HttpContext.Request.ContentLength ??
                JsonSerializer.SerializeToUtf8Bytes(request).LongLength;
            if (operation.MaxRequestSizeBytes <= 0)
            {
                return await CompleteWithErrorAsync(
                    auditLog, StatusCodes.Status503ServiceUnavailable, "configuration_unavailable",
                    "The request cannot be processed at this time.", "Failed",
                    "InvalidRequestSizeConfiguration", correlationId);
            }

            if (incomingBodySize > operation.MaxRequestSizeBytes)
            {
                return await CompleteWithErrorAsync(
                    auditLog, StatusCodes.Status413PayloadTooLarge, "request_too_large",
                    "The request exceeds the configured size limit.", "Rejected",
                    "RequestTooLarge", correlationId);
            }

            RequestValidationResult validation;
            if (operation.RequiresEncryption)
            {
                if (operation.EncryptionProfile is null || request.Json is null)
                {
                    return await CompleteWithErrorAsync(
                        auditLog, StatusCodes.Status503ServiceUnavailable, "encryption_configuration_unavailable",
                        "The request cannot be processed at this time.", "Failed",
                        "EncryptionConfigurationUnavailable", correlationId);
                }

                var decryption = await _payloadDecryptor.TryDecryptAsync(
                    request.Json.Value,
                    operation.EncryptionProfile.KeySourceReference,
                    cancellationToken);

                decryptedPayload = decryption.PlaintextJson;
                if (!decryption.IsValid || decryptedPayload is null)
                {
                    return await CompleteWithErrorAsync(
                        auditLog, StatusCodes.Status400BadRequest, "invalid_encrypted_payload",
                        "The encrypted payload is invalid.", "Rejected",
                        "InvalidEncryptedPayload", correlationId);
                }

                try
                {
                    using var decryptedJson = JsonDocument.Parse(decryptedPayload);
                    validation = _requestValidator.Validate(operation, decryptedJson.RootElement);
                }
                catch (JsonException)
                {
                    return await CompleteWithErrorAsync(
                        auditLog, StatusCodes.Status400BadRequest, "invalid_encrypted_payload",
                        "The encrypted payload is invalid.", "Rejected",
                        "DecryptedPayloadIsNotJson", correlationId);
                }
            }
            else
            {
                validation = _requestValidator.Validate(operation, request.Json);
            }

            if (!validation.IsValid && validation.IsConfigurationIssue)
            {
                return await CompleteWithErrorAsync(
                    auditLog, StatusCodes.Status501NotImplemented, "rule_not_supported",
                    "The configured parameter rules are not supported yet.", "NotImplemented",
                    validation.ErrorCode, correlationId);
            }

            if (!validation.IsValid)
            {
                return await CompleteWithErrorAsync(
                    auditLog, StatusCodes.Status400BadRequest, "invalid_parameters",
                    "One or more request parameters are invalid.", "Rejected",
                    validation.ErrorCode, correlationId);
            }

            outgoingBody = decryptedPayload ??
                Encoding.UTF8.GetBytes(request.Json!.Value.GetRawText());

            forwardingStarted = true;
            var forwardResult = await _httpForwarder.ForwardAsync(
                operation,
                outgoingBody,
                Request.Headers,
                cancellationToken);
            auditLog.UpstreamStatusCode = forwardResult.UpstreamStatusCode;

            if (forwardResult.HasUpstreamResponse)
            {
                await _requestAuditor.TryCompleteAsync(
                    auditLog,
                    forwardResult.ProxyStatusCode,
                    "Forwarded",
                    null,
                    forwardingOccurred: true);

                return new ProxyResponseResult(
                    forwardResult.ProxyStatusCode,
                    forwardResult.ContentType ?? "application/octet-stream",
                    forwardResult.Body ?? []);
            }

            var (errorCode, message) = forwardResult.FailureReason switch
            {
                "RequestTooLarge" => (
                    "request_too_large",
                    "The request exceeds the configured size limit."),
                "UpstreamTimeout" => (
                    "upstream_timeout",
                    "The destination did not respond in time."),
                "UpstreamConnectionFailure" => (
                    "upstream_unavailable",
                    "The destination could not be reached."),
                "UpstreamResponseTooLarge" => (
                    "upstream_response_too_large",
                    "The destination response exceeds the configured size limit."),
                _ => (
                    "forwarding_unavailable",
                    "The request cannot be processed at this time.")
            };

            var outcome = forwardResult.FailureReason == "RequestTooLarge" ? "Rejected" : "Failed";
            return await CompleteWithErrorAsync(
                auditLog,
                forwardResult.ProxyStatusCode,
                errorCode,
                message,
                outcome,
                forwardResult.FailureReason,
                correlationId,
                forwardResult.ForwardingOccurred,
                forwardResult.UpstreamStatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _requestAuditor.TryCompleteAsync(
                auditLog, 0, "Cancelled", "CallerCancelled", forwardingStarted);
            throw;
        }
        catch (EncryptionConfigurationException)
        {
            _logger.LogError(
                "Encryption key configuration unavailable. CorrelationId: {CorrelationId}",
                correlationId);

            return await CompleteWithErrorAsync(
                auditLog, StatusCodes.Status503ServiceUnavailable, "encryption_configuration_unavailable",
                "The request cannot be processed at this time.", "Failed",
                "EncryptionConfigurationUnavailable", correlationId);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Proxy request processing failed. CorrelationId: {CorrelationId}, ExceptionType: {ExceptionType}",
                correlationId,
                exception.GetType().Name);

            return await CompleteWithErrorAsync(
                auditLog, StatusCodes.Status503ServiceUnavailable, "configuration_unavailable",
                "The request cannot be processed at this time.", "Failed",
                "ConfigurationUnavailable", correlationId);
        }
        finally
        {
            if (decryptedPayload is not null)
            {
                CryptographicOperations.ZeroMemory(decryptedPayload);
            }

            if (outgoingBody is not null && !ReferenceEquals(outgoingBody, decryptedPayload))
            {
                CryptographicOperations.ZeroMemory(outgoingBody);
            }
        }
    }

    private async Task<IActionResult> CompleteWithErrorAsync(
        RequestLog auditLog,
        int statusCode,
        string code,
        string message,
        string outcome,
        string? failureReason,
        string correlationId,
        bool forwardingOccurred = false,
        int? upstreamStatusCode = null)
    {
        auditLog.UpstreamStatusCode = upstreamStatusCode;
        await _requestAuditor.TryCompleteAsync(
            auditLog,
            statusCode,
            outcome,
            failureReason,
            forwardingOccurred);

        return Error(statusCode, code, message, correlationId);
    }

    private ObjectResult Error(int statusCode, string code, string message, string correlationId) =>
        StatusCode(statusCode, new ApiErrorResponse(code, message, correlationId));
}
