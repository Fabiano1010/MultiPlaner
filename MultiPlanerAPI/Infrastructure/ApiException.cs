namespace MultiPlanerAPI.Infrastructure;

public sealed class ApiException(
    int statusCode,
    string code,
    string message,
    IDictionary<string, string[]>? errors = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public IDictionary<string, string[]>? Errors { get; } = errors;

    public static ApiException Validation(IDictionary<string, string[]> errors)
    {
        return new ApiException(StatusCodes.Status400BadRequest, "validation_failed", "One or more fields are invalid.",
            errors);
    }

    public static ApiException Validation(string field, string message)
    {
        return Validation(new Dictionary<string, string[]> { [field] = [message] });
    }

    public static void RequireVersion(byte[] actual, byte[] expected, string message)
    {
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new ApiException(StatusCodes.Status409Conflict, "concurrency_conflict", message);
        }
    }
}