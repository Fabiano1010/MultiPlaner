using Microsoft.AspNetCore.Http;

namespace MultiPlanerAPI.UnitTests;

internal sealed class CapturingProblemDetailsService : IProblemDetailsService
{
    public ProblemDetailsContext? Last { get; private set; }

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        Last = context;
        return ValueTask.CompletedTask;
    }
}