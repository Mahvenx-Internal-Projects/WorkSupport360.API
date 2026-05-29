using System.Net;
using System.Text.Json;

namespace WorkSupport360.API.Middleware;

public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log)
{
    public async Task Invoke(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (Exception ex)
        {
            log.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            await WriteErrorAsync(ctx, ex);
        }
    }

    private static async Task WriteErrorAsync(HttpContext ctx, Exception ex)
    {
        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode  = ex switch
        {
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            KeyNotFoundException        => (int)HttpStatusCode.NotFound,
            ArgumentException           => (int)HttpStatusCode.BadRequest,
            InvalidOperationException   => (int)HttpStatusCode.Conflict,
            _                           => (int)HttpStatusCode.InternalServerError
        };
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            message   = ex.Message,
            errorType = ex.GetType().Name,
            timestamp = DateTime.UtcNow,
        }));
    }
}
