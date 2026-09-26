using System.Net;
using System.Text.Json;
using Kue.Api.Dtos.Common;

namespace Kue.Api.Middlewares;

/// <summary>
/// Catches unhandled exceptions in the ASP.NET Core pipeline, logs details securely on the server,
/// and returns standardized, sanitized JSON error responses without leaking stack traces or internal DB structure.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred during request execution: {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response has already started; cannot write global exception payload.");
            return;
        }

        context.Response.ContentType = "application/json; charset=utf-8";

        var (statusCode, message) = exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized access."),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "The requested resource was not found."),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request."),
            InvalidOperationException invEx => (StatusCodes.Status400BadRequest, invEx.Message),
            _ => (StatusCodes.Status500InternalServerError, _env.IsDevelopment()
                ? $"Internal server error: {exception.Message}"
                : "An unexpected internal server error occurred. Please try again later.")
        };

        context.Response.StatusCode = statusCode;

        var responsePayload = new MessageResponseDto(message);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(responsePayload, jsonOptions));
    }
}

public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
