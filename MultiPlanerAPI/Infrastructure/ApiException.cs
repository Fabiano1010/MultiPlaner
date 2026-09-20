namespace MultiPlanerAPI.Infrastructure;

public sealed class ApiException(
    int statusCode, string code, string message, IDictionary<string, string[]>? errors = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public IDictionary<string, string[]>? Errors { get; } = errors;

    public static ApiException Validation(IDictionary<string, string[]> errors) =>
        new(StatusCodes.Status400BadRequest, "validation_failed", "One or more fields are invalid.", errors);
}
