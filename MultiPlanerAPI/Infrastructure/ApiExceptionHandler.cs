using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MultiPlanerAPI.Infrastructure;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            return false;

        var apiError = exception as ApiException;
        var status = apiError?.StatusCode ??
            (exception is DbUpdateConcurrencyException ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError);
        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled API error. TraceId: {TraceId}", context.TraceIdentifier);

        ProblemDetails detail = apiError?.Errors is { } errors
            ? new ValidationProblemDetails(errors)
            : new ProblemDetails();
        detail.Status = status;
        detail.Title = Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status);
        detail.Detail = apiError?.Message ?? (status == 409
            ? "The resource changed. Reload it and retry."
            : "An unexpected server error occurred.");
        detail.Extensions["code"] = apiError?.Code ?? (status == 409 ? "concurrency_conflict" : "internal_error");
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = detail });
        return true;
    }
}
