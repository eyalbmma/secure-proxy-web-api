using Microsoft.AspNetCore.Mvc;

namespace MoD.SecureProxy.Models;

public sealed class ProxyResponseResult(int statusCode, string contentType, byte[] body) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.ContentLength = body.LongLength;

        await response.Body.WriteAsync(body, context.HttpContext.RequestAborted);
    }
}
