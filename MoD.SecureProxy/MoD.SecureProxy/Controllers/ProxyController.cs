using System.Security.Cryptography;
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
    private readonly ILogger<ProxyController> _logger;

    public ProxyController(
        OperationLookup operationLookup,
        RequestAuditor requestAuditor,
        RequestValidator requestValidator,
        AesGcmPayloadDecryptor payloadDecryptor,
        ILogger<ProxyController> logger)
    {
        _operationLookup = operationLookup;
        _requestAuditor = requestAuditor;
        _requestValidator = requestValidator;
        _payloadDecryptor = payloadDecryptor;
        _logger = logger;
    }

    [HttpPost("execute")]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status501NotImplemented)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
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

                var plaintextJson = decryption.PlaintextJson;
                if (!decryption.IsValid || plaintextJson is null)
                {
                    return await CompleteWithErrorAsync(
                        auditLog, StatusCodes.Status400BadRequest, "invalid_encrypted_payload",
                        "The encrypted payload is invalid.", "Rejected",
                        "InvalidEncryptedPayload", correlationId);
                }

                try
                {
                    using var decryptedJson = JsonDocument.Parse(plaintextJson);
                    validation = _requestValidator.Validate(operation, decryptedJson.RootElement);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintextJson);
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

            // Intentionally do not return success before a request is actually forwarded.
            return await CompleteWithErrorAsync(
                auditLog, StatusCodes.Status501NotImplemented, "not_implemented",
                "Proxy forwarding is not implemented yet.", "NotImplemented",
                "ForwardingNotImplemented", correlationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _requestAuditor.TryCompleteAsync(
                auditLog, 0, "Cancelled", "CallerCancelled", forwardingOccurred: false);
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
    }

    private async Task<IActionResult> CompleteWithErrorAsync(
        RequestLog auditLog,
        int statusCode,
        string code,
        string message,
        string outcome,
        string? failureReason,
        string correlationId)
    {
        await _requestAuditor.TryCompleteAsync(
            auditLog,
            statusCode,
            outcome,
            failureReason,
            forwardingOccurred: false);

        return Error(statusCode, code, message, correlationId);
    }

    private ObjectResult Error(int statusCode, string code, string message, string correlationId) =>
        StatusCode(statusCode, new ApiErrorResponse(code, message, correlationId));
}
