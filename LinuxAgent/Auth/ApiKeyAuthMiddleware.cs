using System.Security.Cryptography;
using System.Text;

namespace LinuxAgent.Auth;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private const string ApiKeyHeaderName = "X-Agent-Key";
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;
    private string? _cachedKey;

    public ApiKeyAuthMiddleware(RequestDelegate next, ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("API Key missing");
            return;
        }

        var currentApiKey = GetCurrentApiKey();
        
        if (string.IsNullOrEmpty(currentApiKey))
        {
             _logger.LogError("Server configuration error: No API Key found on server.");
             context.Response.StatusCode = 500;
             await context.Response.WriteAsync("Server configuration error");
             return;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(currentApiKey),
                Encoding.UTF8.GetBytes(extractedApiKey.ToString())))
        {
            _logger.LogWarning("Unauthorized access attempt. Invalid API Key.");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        await _next(context);
    }

    private string? GetCurrentApiKey()
    {
        if (!string.IsNullOrEmpty(_cachedKey)) return _cachedKey;

        // Priority 1: Configuration (Env Var or appsettings) mainly for dev
        var configKey = Environment.GetEnvironmentVariable("AGENT_API_KEY");
        if (!string.IsNullOrEmpty(configKey))
        {
            _cachedKey = configKey;
            return _cachedKey;
        }

        // Priority 2: File store (Production)
        const string keyPath = "/etc/linux-agent/client_secret";
        if (File.Exists(keyPath))
        {
            try 
            {
                var fileKey = File.ReadAllText(keyPath).Trim();
                if(!string.IsNullOrEmpty(fileKey))
                {
                    _cachedKey = fileKey;
                    return _cachedKey;
                }
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to read API key from {Path}", keyPath);
            }
        }

        return null;
    }
}
