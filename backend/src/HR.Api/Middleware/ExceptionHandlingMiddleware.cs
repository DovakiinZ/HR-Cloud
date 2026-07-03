using System.Text.Json;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Models;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance.StateMachine;

namespace HR.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, response) = exception switch
        {
            ValidationException validationEx => ((int)StatusCodes.Status400BadRequest,
                (object)ApiResponse.Fail("Validation failed", validationEx.Errors.SelectMany(e => e.Value).ToList())),
            NotFoundException => (StatusCodes.Status404NotFound,
                (object)ApiResponse.Fail(exception.Message)),
            ForbiddenException => (StatusCodes.Status403Forbidden,
                (object)ApiResponse.Fail(exception.Message)),
            ConflictException => (StatusCodes.Status409Conflict,
                (object)ApiResponse.Fail(exception.Message)),
            // Illegal lifecycle moves (run / transaction state machines) are a conflict, not a crash.
            InvalidStateTransitionException => (StatusCodes.Status409Conflict,
                (object)ApiResponse.Fail(exception.Message)),
            InvalidPayrollTransactionStateException => (StatusCodes.Status409Conflict,
                (object)ApiResponse.Fail(exception.Message)),
            // Structured 422: a closed payroll period blocks the operation.
            // Returns the machine-readable payload so the client (and SP6 amendment flow) can act on it.
            PayrollPeriodClosedException ppce => (StatusCodes.Status422UnprocessableEntity,
                (object)new
                {
                    success = false,
                    message = ppce.Message,
                    errorCode = ppce.Payload.ErrorCode,
                    data = ppce.Payload
                }),
            // Explicit business-rule violations carry a user-facing reason.
            DomainException => (StatusCodes.Status422UnprocessableEntity,
                (object)ApiResponse.Fail(exception.Message)),
            // Safety net: the engine/service layer signals business rules (inactive type,
            // amount<0, duplicate code, …) via InvalidOperationException. Surface the real
            // reason as 422 instead of an opaque 500. Logged as a warning, not swallowed.
            InvalidOperationException => (StatusCodes.Status422UnprocessableEntity,
                (object)ApiResponse.Fail(exception.Message)),
            _ => (StatusCodes.Status500InternalServerError,
                (object)ApiResponse.Fail("An unexpected error occurred"))
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception");
        else if (statusCode == StatusCodes.Status422UnprocessableEntity && exception is InvalidOperationException)
            _logger.LogWarning(exception, "Business-rule violation surfaced as 422 (consider migrating to DomainException)");

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await context.Response.WriteAsync(json);
    }
}
