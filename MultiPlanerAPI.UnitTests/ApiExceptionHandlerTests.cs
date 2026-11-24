using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MultiPlanerAPI.Infrastructure;

namespace MultiPlanerAPI.UnitTests;

public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public async Task Maps413BadHttpRequestExceptionWithoutErrorLog()
    {
        var problems = new CapturingProblemDetailsService();
        var logger = new CapturingLogger<ApiExceptionHandler>();
        var handler = new ApiExceptionHandler(problems, logger);
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            context,
            new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal("payload_too_large", problems.Last?.ProblemDetails.Extensions["code"]);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task Maps400BadHttpRequestExceptionWithoutErrorLog()
    {
        var problems = new CapturingProblemDetailsService();
        var logger = new CapturingLogger<ApiExceptionHandler>();
        var handler = new ApiExceptionHandler(problems, logger);
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            context,
            new BadHttpRequestException("Malformed request.", StatusCodes.Status400BadRequest),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("bad_request", problems.Last?.ProblemDetails.Extensions["code"]);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task MapsUnexpectedExceptionTo500AndLogsError()
    {
        var problems = new CapturingProblemDetailsService();
        var logger = new CapturingLogger<ApiExceptionHandler>();
        var handler = new ApiExceptionHandler(problems, logger);
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("Boom"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("internal_error", problems.Last?.ProblemDetails.Extensions["code"]);
        Assert.Single(logger.Errors);
    }

    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Last { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Last = context;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}
