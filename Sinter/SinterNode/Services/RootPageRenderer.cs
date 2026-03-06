using System.Net;
using System.Text;
using SinterNode.Models;

namespace SinterNode.Services;

public interface IRootPageRenderer
{
    string Render(NodeDashboard dashboard);
}

public sealed class RootPageRenderer : IRootPageRenderer
{
    public string Render(NodeDashboard dashboard)
    {
        var prefixes = string.Join(Environment.NewLine, dashboard.Snapshot.State.ServicePrefixes);
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>SinterNode</title><style>");
        builder.AppendLine("body{font-family:Segoe UI,Helvetica,Arial,sans-serif;background:linear-gradient(135deg,#ece7df,#f7f4ef);margin:0;color:#1e293b;}main{max-width:1100px;margin:0 auto;padding:32px 20px 72px;}h1,h2{margin:0 0 12px;}section{background:rgba(255,255,255,.82);border:1px solid rgba(15,23,42,.08);border-radius:18px;padding:22px;margin-top:18px;box-shadow:0 14px 36px rgba(15,23,42,.08);}code,pre{font-family:Consolas,monospace;}table{width:100%;border-collapse:collapse;}th,td{text-align:left;padding:10px;border-bottom:1px solid rgba(15,23,42,.08);vertical-align:top;}textarea,input{width:100%;box-sizing:border-box;padding:12px;border-radius:12px;border:1px solid rgba(15,23,42,.18);background:#fff;}button{padding:12px 18px;border:0;border-radius:12px;background:#c2410c;color:#fff;font-weight:700;cursor:pointer;}small{color:#475569;} .pill{display:inline-block;padding:4px 8px;border-radius:999px;background:#e2e8f0;margin-right:8px;margin-bottom:8px;} .secret{background:#111827;color:#f8fafc;padding:16px;border-radius:12px;} .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:14px;} .muted{color:#64748b;} </style></head><body><main>");
        builder.AppendLine("<section><h1>SinterNode</h1><p class=\"muted\">Node bootstrap, service discovery, and deployment management for Sinter.</p><div class=\"grid\">");
        builder.AppendLine($"<div><small>Hostname</small><div>{Encode(dashboard.Hostname)}</div></div>");
        builder.AppendLine($"<div><small>OS</small><div>{Encode(dashboard.OsDescription)}</div></div>");
        builder.AppendLine($"<div><small>Architecture</small><div>{Encode(dashboard.ProcessArchitecture)}</div></div>");
        builder.AppendLine($"<div><small>Runtime</small><div>{Encode(dashboard.FrameworkDescription)}</div></div>");
        builder.AppendLine($"<div><small>Node ID</small><div>{Encode(dashboard.Snapshot.State.NodeId.ToString())}</div></div>");
        builder.AppendLine($"<div><small>Uptime</small><div>{Encode(dashboard.Uptime)}</div></div>");
        builder.AppendLine("</div></section>");

        builder.AppendLine("<section><h2>Bootstrap</h2>");
        if (dashboard.Snapshot.ShowApiKey)
        {
            builder.AppendLine("<p>The node key is generated once on first boot. Save it before leaving this page.</p>");
            builder.AppendLine($"<div class=\"secret\"><strong>X-Sinter-Key</strong><br><code>{Encode(dashboard.Snapshot.ApiKey)}</code></div>");
        }
        else
        {
            builder.AppendLine("<p>The bootstrap key is now hidden. Enter it below to update the prefix list later.</p>");
        }

        builder.AppendLine("<form method=\"post\" action=\"/ui/configure\">\n<label for=\"prefixes\">Service prefixes</label><br><small>One per line or comma-separated.</small><br><textarea id=\"prefixes\" name=\"prefixes\" rows=\"6\">" + Encode(prefixes) + "</textarea><br><br><label for=\"apiKey\">API key</label><br><input id=\"apiKey\" name=\"apiKey\" type=\"password\" placeholder=\"Required after bootstrap\"><br><br><button type=\"submit\">Save prefixes</button></form>");
        builder.AppendLine("</section>");

        builder.AppendLine("<section><h2>Configured prefixes</h2>");
        if (dashboard.Snapshot.State.ServicePrefixes.Length == 0)
        {
            builder.AppendLine("<p class=\"muted\">No prefixes configured yet.</p>");
        }
        else
        {
            foreach (var prefix in dashboard.Snapshot.State.ServicePrefixes)
            {
                builder.AppendLine($"<span class=\"pill\">{Encode(prefix)}</span>");
            }
        }

        builder.AppendLine("</section>");
        builder.AppendLine("<section><h2>Discovered services</h2><table><thead><tr><th>Name</th><th>Description</th><th>Managed</th><th>Override</th></tr></thead><tbody>");
        if (dashboard.Services.Count == 0)
        {
            builder.AppendLine("<tr><td colspan=\"4\" class=\"muted\">No matching services found in /etc/systemd/system.</td></tr>");
        }
        else
        {
            foreach (var service in dashboard.Services)
            {
                builder.AppendLine($"<tr><td>{Encode(service.Name)}</td><td>{Encode(service.Description)}</td><td>{(service.IsManagedByNode ? "Yes" : "No")}</td><td>{(service.HasOverride ? "Yes" : "No")}</td></tr>");
            }
        }

        builder.AppendLine("</tbody></table></section>");
        builder.AppendLine("<section><h2>Managed applications</h2><table><thead><tr><th>App</th><th>Repo</th><th>Service</th><th>Last deploy</th></tr></thead><tbody>");
        if (dashboard.ManagedApplications.Count == 0)
        {
            builder.AppendLine("<tr><td colspan=\"4\" class=\"muted\">No managed applications have been deployed yet.</td></tr>");
        }
        else
        {
            foreach (var app in dashboard.ManagedApplications)
            {
                builder.AppendLine($"<tr><td>{Encode(app.AppName)}</td><td>{Encode(app.RepoUrl)}</td><td>{Encode(app.ServiceName)}</td><td>{Encode(app.LastDeploymentUtc?.ToString("u") ?? string.Empty)}</td></tr>");
            }
        }

        builder.AppendLine("</tbody></table></section>");
        builder.AppendLine("</main></body></html>");
        return builder.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}