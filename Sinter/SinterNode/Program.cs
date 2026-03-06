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
builder.Services.AddSingleton<ISystemdOverrideValidator, SystemdOverrideValidator>();
builder.Services.AddSingleton<ISelfUpdateCoordinator, SelfUpdateCoordinator>();
builder.Services.AddSingleton<IServiceCatalog, ServiceCatalog>();
builder.Services.AddSingleton<IManagedApplicationService, ManagedApplicationService>();
builder.Services.AddSingleton<INodeTelemetryCollector, NodeTelemetryCollector>();
builder.Services.AddSingleton<INodeSummaryService, NodeSummaryService>();

var app = builder.Build();

app.UseMiddleware<ApiKeyProtectionMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { Status = "ok" }));

app.MapGet("/ui/state", async (INodeSummaryService summaryService, CancellationToken cancellationToken) =>
	Results.Ok(await summaryService.GetDashboardAsync(includeApiKey: true, cancellationToken)));

app.MapPost("/ui/configure", async Task<IResult> (
	ConfigureNodeRequest request,
	INodeStateStore stateStore,
	CancellationToken cancellationToken) =>
{
	var prefixes = (request.Prefixes ?? [])
		.SelectMany(static value => (value ?? string.Empty).Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.ToArray();

	var state = await stateStore.GetSnapshotAsync(cancellationToken);
	var postedKey = request.ApiKey?.Trim();

	if (state.State.BootstrapCompleted && !await stateStore.ValidateApiKeyAsync(postedKey, cancellationToken))
	{
		return Results.Unauthorized();
	}

	if (prefixes.Length == 0)
	{
		return Results.BadRequest(new { Error = "At least one prefix is required." });
	}

	return Results.Ok(await stateStore.UpdatePrefixesAsync(prefixes, cancellationToken));
});

app.MapPost("/ui/self-update", async Task<IResult> (
	UiSelfUpdateRequest? request,
	INodeStateStore stateStore,
	IManagedApplicationService managedApplicationService,
	IOptions<NodeOptions> options,
	CancellationToken cancellationToken) =>
{
	var state = await stateStore.GetSnapshotAsync(cancellationToken);
	var postedKey = request?.ApiKey?.Trim();

	if (state.State.BootstrapCompleted && !await stateStore.ValidateApiKeyAsync(postedKey, cancellationToken))
	{
		return Results.Json(new { Error = "A valid API key is required." }, statusCode: StatusCodes.Status401Unauthorized);
	}

	var updateRequest = new SelfUpdateRequest(options.Value.DefaultSourceRepository, "main", options.Value.SelfProjectPath, null);
	var events = await CollectOperationEventsAsync(managedApplicationService.SelfUpdateAsync(updateRequest, cancellationToken), cancellationToken);
	var last = events.LastOrDefault();
	var status = last?.Type == "error" ? "Error" : "Success";
	var summary = last?.Message ?? "Self-update request submitted.";
	return Results.Ok(new { Status = status, Summary = summary, Events = events });
});

app.MapGet("/api/status", async (INodeSummaryService summaryService, CancellationToken cancellationToken) =>
	Results.Ok(await summaryService.GetDashboardAsync(includeApiKey: false, cancellationToken)));

app.MapGet("/api/services", async (INodeSummaryService summaryService, CancellationToken cancellationToken) =>
	Results.Ok((await summaryService.GetDashboardAsync(includeApiKey: false, cancellationToken)).Services));

app.MapGet("/api/apps", async (IManagedApplicationService managedApplicationService, CancellationToken cancellationToken) =>
	Results.Ok(await managedApplicationService.ListAsync(cancellationToken)));

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

	try
	{
		await serviceCatalog.WriteUnitFileAsync(serviceName, request.Content, request.AllowOverwriteUnmanaged, cancellationToken);
		return Results.Ok();
	}
	catch (InvalidOperationException ex)
	{
		return Results.BadRequest(new { Error = ex.Message });
	}
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

	try
	{
		await serviceCatalog.WriteOverrideFileAsync(serviceName, request.Content ?? string.Empty, cancellationToken);
		return Results.Ok();
	}
	catch (InvalidOperationException ex)
	{
		return Results.BadRequest(new { Error = ex.Message });
	}
});

app.MapPost("/api/services/{serviceName}/restart", async Task (
	string serviceName,
	HttpContext context,
	IServiceCatalog serviceCatalog,
	CancellationToken cancellationToken) =>
{
	await context.WriteNdjsonAsync(serviceCatalog.RestartServiceAsync(serviceName, cancellationToken), cancellationToken);
});

app.MapPost("/api/services/{serviceName}/start", async Task (
	string serviceName,
	HttpContext context,
	IServiceCatalog serviceCatalog,
	CancellationToken cancellationToken) =>
{
	await context.WriteNdjsonAsync(serviceCatalog.StartServiceAsync(serviceName, cancellationToken), cancellationToken);
});

app.MapPost("/api/services/{serviceName}/stop", async Task (
	string serviceName,
	HttpContext context,
	IServiceCatalog serviceCatalog,
	CancellationToken cancellationToken) =>
{
	await context.WriteNdjsonAsync(serviceCatalog.StopServiceAsync(serviceName, cancellationToken), cancellationToken);
});

app.MapPost("/api/services/{serviceName}/enable", async Task (
	string serviceName,
	HttpContext context,
	IServiceCatalog serviceCatalog,
	CancellationToken cancellationToken) =>
{
	await context.WriteNdjsonAsync(serviceCatalog.EnableServiceAsync(serviceName, cancellationToken), cancellationToken);
});

app.MapPost("/api/services/{serviceName}/disable", async Task (
	string serviceName,
	HttpContext context,
	IServiceCatalog serviceCatalog,
	CancellationToken cancellationToken) =>
{
	await context.WriteNdjsonAsync(serviceCatalog.DisableServiceAsync(serviceName, cancellationToken), cancellationToken);
});

app.MapPost("/api/system/daemon-reload", async Task (
	HttpContext context,
	ISystemServiceManager systemServiceManager,
	CancellationToken cancellationToken) =>
{
	await context.WriteNdjsonAsync(ReloadDaemonAsync(systemServiceManager, cancellationToken), cancellationToken);

	static async IAsyncEnumerable<OperationEvent> ReloadDaemonAsync(
		ISystemServiceManager systemServiceManager,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		yield return OperationEvent.Info("Running systemd daemon-reload.", "systemd");
		await systemServiceManager.DaemonReloadAsync(cancellationToken);
		yield return OperationEvent.Success("systemd daemon-reload completed.", "systemd");
	}
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

static async Task<List<OperationEvent>> CollectOperationEventsAsync(IAsyncEnumerable<OperationEvent> stream, CancellationToken cancellationToken)
{
	var events = new List<OperationEvent>();
	await foreach (var evt in stream.WithCancellation(cancellationToken))
	{
		events.Add(evt);
	}

	return events;
}

app.Run();

public partial class Program;
