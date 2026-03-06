using Microsoft.AspNetCore.Http.Extensions;

namespace SinterNode.Services;

public sealed class ApiKeyProtectionMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Sinter-Key";

    public async Task InvokeAsync(HttpContext context, INodeStateStore stateStore)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/api/status", StringComparison.OrdinalIgnoreCase) ||
            HttpMethods.IsGet(context.Request.Method) && context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var providedKey) ||
            !await stateStore.ValidateApiKeyAsync(providedKey.ToString(), context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                Error = "A valid API key is required.",
                Header = HeaderName,
                Path = context.Request.GetDisplayUrl()
            }, context.RequestAborted);
            return;
        }

        await next(context);
    }
}