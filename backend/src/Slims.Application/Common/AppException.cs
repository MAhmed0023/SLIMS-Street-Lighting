namespace Slims.Application.Common;

/// <summary>An error the API returns to the client with an HTTP status (400 validation, 401 sign-in, 403 blocked, 404 missing, 409 rule, 423 locked).</summary>
public class AppException(int status, string message, string? step = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Step { get; } = step;
    public static AppException NotFound(string what) => new(404, $"{what} not found");
    public static AppException Forbidden(string message, string? step = null) => new(403, message, step);
    public static AppException Bad(string message, string? step = null) => new(400, message, step);
    /// <summary>A business rule (BR-nnn) refused the action.</summary>
    public static AppException Rule(string rule, string message) => new(409, message, rule);
}
