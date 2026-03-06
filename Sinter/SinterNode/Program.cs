using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SinterNode.Models;
using SinterNode.Options;
using SinterNode.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<NodeOptions>(builder.Configuration.GetSection(NodeOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<INodeStateStore, NodeStateStore>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IReleasePointerManager, SymlinkReleasePointerManager>();
builder.Services.AddSingleton<ISystemServiceManager, SystemServiceManager>();
builder.Services.AddSingleton<IOperationLockProvider, OperationLockProvider>();
builder.Services.AddSingleton<IServiceCatalog, ServiceCatalog>();
builder.Services.AddSingleton<IManagedApplicationService, ManagedApplicationService>();
builder.Services.AddSingleton<INodeSummaryService, NodeSummaryService>();
builder.Services.AddSingleton<IRootPageRenderer, RootPageRenderer>();

var app = builder.Build();

app.UseMiddleware<ApiKeyProtectionMiddleware>();

app.MapGet("/health", () => Results.Ok(new { Status = "ok" }));

app.MapGet("/", async Task<ContentHttpResult> (
	HttpContext context,
	INodeSummaryService summaryService,
	IRootPageRenderer renderer,
	CancellationToken cancellationToken) =>
{
	var includeApiKey = !context.Request.Query.TryGetValue("adminKey", out var queryKey) || string.IsNullOrWhiteSpace(queryKey);
	var dashboard = await summaryService.GetDashboardAsync(includeApiKey, cancellationToken);
	var html = renderer.Render(dashboard);
	return TypedResults.Content(html, "text/html", Encoding.UTF8);
});

app.MapPost("/ui/configure", async Task<Results<RedirectHttpResult, BadRequest<string>, UnauthorizedHttpResult>> (
	HttpContext context,
	INodeStateStore stateStore,
	CancellationToken cancellationToken) =>
{
	var form = await context.Request.ReadFormAsync(cancellationToken);
	var prefixes = form["prefixes"]
		.SelectMany(static value => (value ?? string.Empty).Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.ToArray();

	var state = await stateStore.GetSnapshotAsync(cancellationToken);
	var postedKey = form["apiKey"].ToString();

	if (state.State.BootstrapCompleted && !await stateStore.ValidateApiKeyAsync(postedKey, cancellationToken))
	{
		return TypedResults.Unauthorized();
	}

	if (prefixes.Length == 0)
	{
		return TypedResults.BadRequest("At least one prefix is required.");
	}

	await stateStore.UpdatePrefixesAsync(prefixes, cancellationToken);
	return TypedResults.Redirect("/");
});

app.MapGet("/api/status", async (INodeSummaryService summaryService, CancellationToken cancellationToken) =>
	Results.Ok(await summaryService.GetDashboardAsync(includeApiKey: false, cancellationToken)));

app.MapGet("/api/services", async (INodeSummaryService summaryService, CancellationToken cancellationToken) =>
	Results.Ok((await summaryService.GetDashboardAsync(includeApiKey: false, cancellationToken)).Services));

app.MapPut("/api/prefixes", async Task<IResult> (
	HttpContext context,
	INodeStateStore stateStore,
	CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpdatePrefixesRequest>(cancellationToken);
	if (request?.Prefixes is null || request.Prefixes.Length == 0)
	{
		return Results.BadRequest(new { Error = "At least one prefix is required." });
	}

	await stateStore.UpdatePrefixesAsync(request.Prefixes, cancellationToken);
	return Results.Ok(await stateStore.GetSnapshotAsync(cancellationToken));
});

app.MapGet("/api/services/{serviceName}/unit", async Task<IResult> (
	string serviceName,
	IServiceCatalog serviceCatalog,
	CancellationToken cancellationToken) =>
{
	var unit = await serviceCatalog.ReadUnitFileAsync(serviceName, cancellationToken);
	return unit is null ? Results.NotFound() : Results.Text(unit, "text/plain");
});

app.MapPut("/api/services/{serviceName}/unit", async Task<IResult> (
	string serviceName,
	HttpContext context,
	IServiceCatalog serviceCatalog,
	CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpdateServiceFileRequest>(cancellationToken);
	if (request is null || string.IsNullOrWhiteSpace(request.Content))
	{
		return Results.BadRequest(new { Error = "Content is required." });
	}

	await serviceCatalog.WriteUnitFileAsync(serviceName, request.Content, request.AllowOverwriteUnmanaged, cancellationToken);
	return Results.Ok();
});

app.MapGet("/api/services/{serviceName}/override", async Task<IResult> (
	string serviceName,
	IServiceCatalog serviceCatalog,
	CancellationToken cancellationToken) =>
{
	var unit = await serviceCatalog.ReadOverrideFileAsync(serviceName, cancellationToken);
	return unit is null ? Results.NotFound() : Results.Text(unit, "text/plain");
});

app.MapPut("/api/services/{serviceName}/override", async Task<IResult> (
	string serviceName,
	HttpContext context,
	IServiceCatalog serviceCatalog,
	CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpdateServiceFileRequest>(cancellationToken);
	if (request is null)
	{
		return Results.BadRequest(new { Error = "Invalid payload." });
	}

	await serviceCatalog.WriteOverrideFileAsync(serviceName, request.Content ?? string.Empty, cancellationToken);
	return Results.Ok();
});

app.MapPost("/api/services/{serviceName}/restart", async Task (
	string serviceName,
	HttpContext context,
	IServiceCatalog serviceCatalog,
	CancellationToken cancellationToken) =>
{
	await context.WriteNdjsonAsync(serviceCatalog.RestartServiceAsync(serviceName, cancellationToken), cancellationToken);
});

app.MapPost("/api/apps/deploy", async Task (
	HttpContext context,
	IManagedApplicationService managedApplicationService,
	CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<DeployApplicationRequest>(cancellationToken);
	if (request is null)
	{
		context.Response.StatusCode = StatusCodes.Status400BadRequest;
		await context.Response.WriteAsJsonAsync(new { Error = "Invalid payload." }, cancellationToken);
		return;
	}

	await context.WriteNdjsonAsync(managedApplicationService.DeployAsync(request, cancellationToken), cancellationToken);
});

app.MapPost("/api/apps/{appName}/restart", async Task (
	string appName,
	HttpContext context,
	IManagedApplicationService managedApplicationService,
	CancellationToken cancellationToken) =>
{
	await context.WriteNdjsonAsync(managedApplicationService.RestartAsync(appName, cancellationToken), cancellationToken);
});

app.MapDelete("/api/apps/{appName}", async Task (
	string appName,
	HttpContext context,
	IManagedApplicationService managedApplicationService,
	CancellationToken cancellationToken) =>
{
	await context.WriteNdjsonAsync(managedApplicationService.UninstallAsync(appName, cancellationToken), cancellationToken);
});

app.MapPost("/api/node/self-update", async Task (
	HttpContext context,
	IManagedApplicationService managedApplicationService,
	IOptions<NodeOptions> options,
	CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<SelfUpdateRequest>(cancellationToken) ??
		new SelfUpdateRequest(options.Value.DefaultSourceRepository, "main", options.Value.SelfProjectPath, null);
	await context.WriteNdjsonAsync(managedApplicationService.SelfUpdateAsync(request, cancellationToken), cancellationToken);
});

app.Run();

public partial class Program;
