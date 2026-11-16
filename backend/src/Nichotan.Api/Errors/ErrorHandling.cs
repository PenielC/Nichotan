using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Application.Common;
using Nichotan.Application.Identity;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;

namespace Nichotan.Api.Errors;

/// <summary>
/// Turns exceptions into RFC 7807 problem responses. Expected errors carry a message meant for the user;
/// anything unexpected becomes a generic 500 and is logged without request bodies.
/// </summary>
public class ExceptionMapper(ILogger<ExceptionMapper> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            RequestValidationException => (StatusCodes.Status400BadRequest, "Invalid request", exception.Message),
            DomainException => (StatusCodes.Status400BadRequest, "Request refused", exception.Message),
            AuthenticationFailedException => (StatusCodes.Status401Unauthorized, "Not signed in", exception.Message),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden", exception.Message),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found", exception.Message),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", exception.Message),
            AccountLockedException => (StatusCodes.Status429TooManyRequests, "Too many attempts", exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "Server error", "Something went wrong. Please try again."),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        if (exception is ForbiddenException && context.User.Identity?.IsAuthenticated == true)
        {
            // Refusals from inside handlers are audited like policy refusals.
            context.RequestServices.GetRequiredService<IAuditLog>()
                .Add(AuditAction.AccessDenied, AuditOutcome.Denied, details: new { method = context.Request.Method, path = context.Request.Path.Value });
            await context.RequestServices.GetRequiredService<IAppDbContext>().SaveChangesAsync(cancellationToken);
        }

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        if (exception is RequestValidationException validation) problem.Extensions["errors"] = validation.Errors;
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }
}
