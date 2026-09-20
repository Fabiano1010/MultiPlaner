using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MultiPlanerAPI.Infrastructure;

// Covers JSON mutations as well as forms, including anonymous login/register.
public sealed class CsrfValidationFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return;

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid CSRF token",
                Detail = "Get a token from /api/auth/csrf and send it in the X-CSRF-TOKEN header.",
                Extensions = { ["code"] = "invalid_csrf", ["traceId"] = context.HttpContext.TraceIdentifier }
            };
            context.Result = new ObjectResult(problem)
            {
                StatusCode = problem.Status,
                ContentTypes = { "application/problem+json" }
            };
        }
    }
}
