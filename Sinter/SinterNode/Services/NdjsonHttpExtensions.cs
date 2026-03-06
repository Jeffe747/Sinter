using System.Text.Json;
using SinterNode.Models;

namespace SinterNode.Services;

public static class NdjsonHttpExtensions
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteNdjsonAsync(this HttpContext context, IAsyncEnumerable<OperationEvent> events, CancellationToken cancellationToken)
    {
        context.Response.ContentType = "application/x-ndjson";
        await foreach (var evt in events.WithCancellation(cancellationToken))
        {
            await JsonSerializer.SerializeAsync(context.Response.Body, evt, SerializerOptions, cancellationToken);
            await context.Response.WriteAsync("\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }
}