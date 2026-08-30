namespace UserManagement.UI.Services;

/// <summary>Thrown when the API returns a non-success response; carries the message and field errors for display.</summary>
public class ApiException : Exception
{
    public IDictionary<string, string[]>? Errors { get; }
    public int? StatusCode { get; }

    public ApiException(string message, int? statusCode = null, IDictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}
