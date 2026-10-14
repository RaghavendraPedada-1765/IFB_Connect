using IFB.Server.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace IFB.Server.Middleware;

/// <summary>
/// Audits state-changing API calls plus sensitive reads (administration and file downloads).
/// Routine reads such as directory searches and history pages are not audited, which keeps the
/// audit table meaningful and bounded alongside Retention:AuditDays.
/// </summary>
public sealed class AuditFilter(IAuditService audit) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var result = await next();
        var http = context.HttpContext;
        if (!ShouldAudit(http.Request)) return;
        await audit.LogAsync(http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            $"{http.Request.Method} {context.ActionDescriptor.DisplayName}",
            $"status={(result.Exception != null ? 500 : (result.Result as Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)?.StatusCode ?? 200)}", http.Connection.RemoteIpAddress?.ToString());
    }

    private static bool ShouldAudit(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return true;
        var path = request.Path;
        return path.StartsWithSegments("/api/v1/admin", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/api/v1/files", StringComparison.OrdinalIgnoreCase);
    }
}
