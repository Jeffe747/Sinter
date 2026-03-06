var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => Results.Text("SinterTest is running.", "text/plain"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();