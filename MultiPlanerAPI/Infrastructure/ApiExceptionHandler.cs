using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MultiPlanerAPI.Infrastructure;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var apiError = exception as ApiException;
        var badRequest = exception as BadHttpRequestException;
        var status = apiError?.StatusCode
                     ?? (badRequest is not null
                         ? (badRequest.StatusCode is >= 400 and < 500
                             ? badRequest.StatusCode
                             : StatusCodes.Status400BadRequest)
                         : exception is DbUpdateConcurrencyException
                             ? StatusCodes.Status409Conflict
                             : StatusCodes.Status500InternalServerError);
        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API error. TraceId: {TraceId}", context.TraceIdentifier);
        }

        var detail = apiError?.Errors is { } errors
            ? new ValidationProblemDetails(errors)
            : new ProblemDetails();
        detail.Status = status;
        detail.Title = Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status);
        detail.Detail = apiError?.Message ?? status switch
        {
            StatusCodes.Status409Conflict => "The resource changed. Reload it and retry.",
            StatusCodes.Status413PayloadTooLarge => "The uploaded file or request body exceeds the maximum allowed size.",
            >= 400 and < 500 when badRequest is not null => "The HTTP request is invalid.",
            _ => "An unexpected server error occurred."
        };
        detail.Extensions["code"] = apiError?.Code ?? status switch
        {
            StatusCodes.Status409Conflict => "concurrency_conflict",
            StatusCodes.Status413PayloadTooLarge => "payload_too_large",
            >= 400 and < 500 when badRequest is not null => "bad_request",
            _ => "internal_error"
        };
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = detail });
        return true;
    }
}