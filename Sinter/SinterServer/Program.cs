using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using SinterServer.Data;
using SinterServer.Models;
using SinterServer.Options;
using SinterServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SinterServerOptions>(builder.Configuration.GetSection(SinterServerOptions.SectionName));
builder.Services.AddDataProtection();
builder.Services.AddDbContext<SinterServerDbContext>((services, options) =>
{
	var settings = services.GetRequiredService<IConfiguration>().GetSection(SinterServerOptions.SectionName).Get<SinterServerOptions>() ?? new SinterServerOptions();
	Directory.CreateDirectory(Path.GetDirectoryName(settings.DatabasePath)!);
	options.UseSqlite($"Data Source={settings.DatabasePath}");
});
builder.Services.AddHttpClient();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IGitCredentialProtector, GitCredentialProtector>();
builder.Services.AddScoped<INodeClient, NodeClient>();
builder.Services.AddScoped<IRegistryService, RegistryService>();
builder.Services.AddHostedService<NodePollingService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
	var db = scope.ServiceProvider.GetRequiredService<SinterServerDbContext>();
	await db.Database.EnsureCreatedAsync();
}

app.UseExceptionHandler(errorApp =>
{
	errorApp.Run(async context =>
	{
		var feature = context.Features.Get<IExceptionHandlerFeature>();
		var exception = feature?.Error;
		context.Response.ContentType = "application/json";
		context.Response.StatusCode = exception is InvalidOperationException or ArgumentException
			? StatusCodes.Status400BadRequest
			: StatusCodes.Status500InternalServerError;
		await context.Response.WriteAsJsonAsync(new
		{
			Error = exception?.Message ?? "An unexpected server error occurred."
		});
	});
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { Status = "ok" }));

app.MapGet("/api/state", async (IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.GetDashboardAsync(cancellationToken)));

app.MapGet("/api/nodes", async (IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.GetNodesAsync(cancellationToken)));

app.MapPost("/api/nodes", async (HttpContext context, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpsertNodeRequest>(cancellationToken);
	if (request is null)
	{
		return Results.BadRequest(new { Error = "Invalid payload." });
	}

	return Results.Ok(await registryService.CreateNodeAsync(request, cancellationToken));
});

app.MapPut("/api/nodes/{nodeId:guid}", async (Guid nodeId, HttpContext context, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpsertNodeRequest>(cancellationToken);
	if (request is null)
	{
		return Results.BadRequest(new { Error = "Invalid payload." });
	}

	return Results.Ok(await registryService.UpdateNodeAsync(nodeId, request, cancellationToken));
});

app.MapDelete("/api/nodes/{nodeId:guid}", async (Guid nodeId, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	await registryService.DeleteNodeAsync(nodeId, cancellationToken);
	return Results.NoContent();
});

app.MapPost("/api/nodes/{nodeId:guid}/refresh", async (Guid nodeId, IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.RefreshNodeAsync(nodeId, cancellationToken)));

app.MapPost("/api/nodes/{nodeId:guid}/daemon-reload", async (Guid nodeId, IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.ReloadDaemonAsync(nodeId, cancellationToken)));

app.MapGet("/api/auth-users", async (IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.GetAuthUsersAsync(cancellationToken)));

app.MapPost("/api/auth-users", async (HttpContext context, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpsertGitCredentialRequest>(cancellationToken);
	if (request is null)
	{
		return Results.BadRequest(new { Error = "Invalid payload." });
	}

	return Results.Ok(await registryService.CreateAuthUserAsync(request, cancellationToken));
});

app.MapPut("/api/auth-users/{credentialId:guid}", async (Guid credentialId, HttpContext context, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpsertGitCredentialRequest>(cancellationToken);
	if (request is null)
	{
		return Results.BadRequest(new { Error = "Invalid payload." });
	}

	return Results.Ok(await registryService.UpdateAuthUserAsync(credentialId, request, cancellationToken));
});

app.MapDelete("/api/auth-users/{credentialId:guid}", async (Guid credentialId, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	await registryService.DeleteAuthUserAsync(credentialId, cancellationToken);
	return Results.NoContent();
});

app.MapGet("/api/apps", async (IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.GetApplicationsAsync(cancellationToken)));

app.MapPost("/api/apps", async (HttpContext context, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpsertApplicationRequest>(cancellationToken);
	if (request is null)
	{
		return Results.BadRequest(new { Error = "Invalid payload." });
	}

	return Results.Ok(await registryService.CreateApplicationAsync(request, cancellationToken));
});

app.MapPut("/api/apps/{applicationId:guid}", async (Guid applicationId, HttpContext context, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpsertApplicationRequest>(cancellationToken);
	if (request is null)
	{
		return Results.BadRequest(new { Error = "Invalid payload." });
	}

	return Results.Ok(await registryService.UpdateApplicationAsync(applicationId, request, cancellationToken));
});

app.MapDelete("/api/apps/{applicationId:guid}", async (Guid applicationId, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	await registryService.DeleteApplicationAsync(applicationId, cancellationToken);
	return Results.NoContent();
});

app.MapPost("/api/apps/{applicationId:guid}/assign", async (Guid applicationId, HttpContext context, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<AssignApplicationRequest>(cancellationToken);
	if (request is null)
	{
		return Results.BadRequest(new { Error = "Invalid payload." });
	}

	return Results.Ok(await registryService.AssignApplicationAsync(applicationId, request, cancellationToken));
});

app.MapPost("/api/apps/{applicationId:guid}/deploy", async (Guid applicationId, IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.DeployApplicationAsync(applicationId, cancellationToken)));

app.MapPost("/api/apps/{applicationId:guid}/redeploy", async (Guid applicationId, IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.DeployApplicationAsync(applicationId, cancellationToken)));

app.MapDelete("/api/apps/{applicationId:guid}/deployment", async (Guid applicationId, IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.UninstallApplicationAsync(applicationId, cancellationToken)));

app.MapPost("/api/apps/{applicationId:guid}/restart-service", async (Guid applicationId, IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.RestartApplicationServiceAsync(applicationId, cancellationToken)));

app.MapGet("/api/apps/{applicationId:guid}/service-unit", async (Guid applicationId, IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.GetServiceUnitAsync(applicationId, cancellationToken)));

app.MapPut("/api/apps/{applicationId:guid}/service-unit", async (Guid applicationId, HttpContext context, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpdateRemoteFileRequest>(cancellationToken);
	if (request is null)
	{
		return Results.BadRequest(new { Error = "Invalid payload." });
	}

	return Results.Ok(await registryService.UpdateServiceUnitAsync(applicationId, request, cancellationToken));
});

app.MapGet("/api/apps/{applicationId:guid}/override", async (Guid applicationId, IRegistryService registryService, CancellationToken cancellationToken) =>
	Results.Ok(await registryService.GetServiceOverrideAsync(applicationId, cancellationToken)));

app.MapPut("/api/apps/{applicationId:guid}/override", async (Guid applicationId, HttpContext context, IRegistryService registryService, CancellationToken cancellationToken) =>
{
	var request = await context.Request.ReadFromJsonAsync<UpdateRemoteFileRequest>(cancellationToken);
	if (request is null)
	{
		return Results.BadRequest(new { Error = "Invalid payload." });
	}

	return Results.Ok(await registryService.UpdateServiceOverrideAsync(applicationId, request, cancellationToken));
});

app.Run();

public partial class Program;
