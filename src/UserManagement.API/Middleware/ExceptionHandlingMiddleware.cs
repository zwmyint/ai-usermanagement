using System.Net;
using System.Text.Json;
using UserManagement.API.Common;
using UserManagement.Domain.Exceptions;
using ValidationException = UserManagement.Domain.Exceptions.ValidationException;

namespace UserManagement.API.Middleware;

/// <summary>
/// Translates domain exceptions into consistent HTTP responses and hides internal details
/// from clients while still logging the full exception server-side.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        var (status, message, errors) = exception switch
        {
            ValidationException ve => (HttpStatusCode.BadRequest, ve.Message, ve.Errors),
            NotFoundException nf => (HttpStatusCode.NotFound, nf.Message, null),
            ConflictException ce => (HttpStatusCode.Conflict, ce.Message, null),
            ForbiddenException fe => (HttpStatusCode.Unauthorized, fe.Message, null),
            DomainException de => (HttpStatusCode.BadRequest, de.Message, null),
            OperationCanceledException => (HttpStatusCode.BadRequest, "The request was cancelled.", null),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.", (IDictionary<string, string[]>?)null)
        };

        if (status == HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", traceId);
        else
            _logger.LogWarning("Handled {ExceptionType}: {Message}. TraceId: {TraceId}",
                exception.GetType().Name, exception.Message, traceId);

        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";

        if (_environment.IsDevelopment() && status == HttpStatusCode.InternalServerError)
            message = exception.Message;

        var payload = ApiResponse.Fail(message, errors, traceId);
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
