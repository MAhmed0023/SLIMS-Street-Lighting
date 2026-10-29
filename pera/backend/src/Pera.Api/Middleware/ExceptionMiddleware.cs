using Pera.Application.Common;

namespace Pera.Api.Middleware;

/// <summary>Turns AppException into {error, step, detail} with its status; anything else into a logged 500.</summary>
public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> log)
{
    public async Task Invoke(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (AppException e)
        {
            ctx.Response.StatusCode = e.Status;
            await ctx.Response.WriteAsJsonAsync(new { error = e.Message, step = e.Step, detail = e.Detail });
        }
        catch (Exception e)
        {
            log.LogError(e, "Unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            if (ctx.Response.HasStarted) throw;
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsJsonAsync(new { error = "Unexpected server error. The incident has been logged.", step = (string?)null, detail = (object?)null });
        }
    }
}
