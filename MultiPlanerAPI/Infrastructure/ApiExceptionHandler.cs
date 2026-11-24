using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace MultiPlanerAPI.Infrastructure;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && ct.IsCancellationRequested)
        {
            return false;
        }

        var (status, code, message, errors) = MapException(exception);
        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API error. TraceId: {TraceId}", context.TraceIdentifier);
        }

        var detail = errors is not null ? new ValidationProblemDetails(errors) : new ProblemDetails();
        detail.Status = status;
        detail.Title = ReasonPhrases.GetReasonPhrase(status);
        detail.Detail = message;
        detail.Extensions["code"] = code;

        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = detail });
        return true;
    }

    private static (int Status, string Code, string Message, IDictionary<string, string[]>? Errors) MapException(
        Exception ex)
    {
        return ex switch
        {
            ApiException api => (api.StatusCode, api.Code, api.Message, api.Errors),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                (StatusCodes.Status413PayloadTooLarge, "payload_too_large",
                    "The uploaded file or request body exceeds the maximum allowed size.", null),
            BadHttpRequestException { StatusCode: >= 400 and < 500 } bad =>
                (bad.StatusCode, "bad_request", "The HTTP request is invalid.", null),
            BadHttpRequestException =>
                (StatusCodes.Status400BadRequest, "bad_request", "The HTTP request is invalid.", null),
            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "concurrency_conflict", "The resource changed. Reload it and retry.",
                    null),
            _ => (StatusCodes.Status500InternalServerError, "internal_error", "An unexpected server error occurred.",
                null)
        };
    }
}