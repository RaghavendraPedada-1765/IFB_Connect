using IFB.Server.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace IFB.Server.Middleware;
public sealed class AuditFilter(IAuditService audit) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var result = await next();
        var http = context.HttpContext;
        await audit.LogAsync(http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            $"{http.Request.Method} {context.ActionDescriptor.DisplayName}",
            $"status={(result.Exception != null ? 500 : (result.Result as Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)?.StatusCode ?? 200)}", http.Connection.RemoteIpAddress?.ToString());
    }
}
