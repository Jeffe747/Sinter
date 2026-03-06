var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5045");

var app = builder.Build();

app.MapGet("/", () => Results.Text("SinterTest is running.", "text/plain"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();