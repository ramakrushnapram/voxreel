using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AIVIDEO.Server.Infrastructure;

/// <summary>
/// Turns database connectivity failures into a clean 503 instead of a 500 carrying a full
/// Npgsql stack trace.
///
/// The stack trace is worth suppressing for two reasons: it tells the caller nothing
/// actionable, and it leaks host names, driver internals, and connection topology to
/// anyone who can reach the API. The detail is logged server-side instead.
/// </summary>
public sealed class DatabaseExceptionHandler(ILogger<DatabaseExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DbException)
        {
            return false;
        }

        logger.LogError(exception, "Database unavailable while handling {Path}.", httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Database unavailable",
            Detail = "The API could not reach PostgreSQL. Check ConnectionStrings:Default and that the server is running.",
            Status = StatusCodes.Status503ServiceUnavailable
        }, cancellationToken);

        return true;
    }
}
