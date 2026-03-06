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
        var listenUrls = dashboard.Environment.ListenUrls.Length == 0
            ? "Configured by the host"
            : string.Join(", ", dashboard.Environment.ListenUrls);
        var bootstrapState = dashboard.Snapshot.State.BootstrapCompleted ? "Ready" : "Needs setup";
        var servicesManagedCount = dashboard.Services.Count(static service => service.IsManagedByNode);
        var servicesWithOverrides = dashboard.Services.Count(static service => service.HasOverride);
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>SinterNode</title><style>");
        builder.AppendLine(":root{color-scheme:light;--bg:#f4f1ea;--panel:#fffdf9;--panel-strong:#fff;--ink:#122033;--muted:#5d6b7c;--line:#d8ddd4;--line-strong:#c5cdc0;--accent:#0f766e;--accent-soft:#d9f2ee;--warn:#b45309;--warn-soft:#fff2df;--code:#0f172a;--success:#166534;--success-soft:#e8f7ec;--shadow:0 18px 44px rgba(18,32,51,.08);}*{box-sizing:border-box;}html{scroll-behavior:smooth;}body{margin:0;background:radial-gradient(circle at top left,#faf6ef 0,#f4f1ea 45%,#ece9e0 100%);color:var(--ink);font-family:Bahnschrift,\"Segoe UI Variable\",\"Segoe UI\",Helvetica,Arial,sans-serif;line-height:1.45;}a{color:inherit;}main{max-width:1180px;margin:0 auto;padding:28px 18px 72px;}section{background:var(--panel);border:1px solid var(--line);border-radius:18px;padding:22px;margin-top:18px;box-shadow:var(--shadow);}header.hero{display:grid;grid-template-columns:1.5fr .9fr;gap:18px;align-items:stretch;}@media (max-width:900px){header.hero{grid-template-columns:1fr;}}h1{font-size:clamp(2rem,4vw,3rem);line-height:1;margin:0 0 10px;}h2{font-size:1.1rem;margin:0 0 12px;}p{margin:0 0 12px;}small,.muted{color:var(--muted);}code,pre,.mono{font-family:Consolas,\"Cascadia Code\",monospace;}table{width:100%;border-collapse:collapse;font-size:.97rem;}caption{text-align:left;font-weight:700;margin-bottom:10px;}th,td{text-align:left;padding:12px 10px;border-bottom:1px solid var(--line);vertical-align:top;}th{font-size:.82rem;text-transform:uppercase;letter-spacing:.04em;color:var(--muted);}tr:last-child td{border-bottom:0;}textarea,input{width:100%;padding:12px 14px;border-radius:12px;border:1px solid var(--line-strong);background:var(--panel-strong);color:var(--ink);font:inherit;}textarea:focus,input:focus,button:focus,.skip-link:focus{outline:3px solid rgba(15,118,110,.2);outline-offset:2px;border-color:var(--accent);}button{padding:12px 18px;border:0;border-radius:12px;background:var(--accent);color:#fff;font-weight:700;font:inherit;cursor:pointer;}button:hover{filter:brightness(.96);}dl.meta{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:14px;margin:0;}dl.meta div{padding:14px;border:1px solid var(--line);border-radius:14px;background:var(--panel-strong);}dt{font-size:.78rem;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);margin-bottom:6px;}dd{margin:0;font-size:1rem;font-weight:600;word-break:break-word;} .skip-link{position:absolute;left:12px;top:12px;transform:translateY(-160%);background:var(--ink);color:#fff;padding:10px 12px;border-radius:10px;text-decoration:none;z-index:10;} .skip-link:focus{transform:translateY(0);} .hero-card{background:linear-gradient(180deg,#fffdf9 0,#f8f5ef 100%);border:1px solid var(--line);border-radius:18px;padding:22px;} .summary-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;margin-top:16px;}@media (max-width:900px){.summary-grid{grid-template-columns:repeat(2,minmax(0,1fr));}}@media (max-width:560px){.summary-grid{grid-template-columns:1fr;}} .stat{padding:16px;border-radius:16px;border:1px solid var(--line);background:var(--panel-strong);} .stat-label{display:block;font-size:.78rem;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);margin-bottom:6px;} .stat-value{display:block;font-size:1.6rem;font-weight:700;line-height:1.1;} .stat-note{display:block;font-size:.88rem;color:var(--muted);margin-top:6px;} .status-pill,.pill{display:inline-flex;align-items:center;gap:8px;padding:6px 10px;border-radius:999px;font-size:.86rem;font-weight:700;border:1px solid transparent;} .pill{background:#edf2f7;color:#243446;border-color:#d6dde6;margin:0 8px 8px 0;} .status-ready{background:var(--success-soft);color:var(--success);border-color:#b9e3c3;} .status-warn{background:var(--warn-soft);color:var(--warn);border-color:#f3cf95;} .key-box{background:var(--code);color:#f8fafc;padding:16px;border-radius:14px;overflow:auto;} .section-head{display:flex;justify-content:space-between;gap:16px;align-items:flex-start;margin-bottom:12px;}@media (max-width:720px){.section-head{flex-direction:column;}} .stack{display:grid;gap:16px;} .compact{font-size:.92rem;} .table-wrap{overflow:auto;border:1px solid var(--line);border-radius:14px;background:var(--panel-strong);} .path{font-size:.84rem;color:var(--muted);word-break:break-all;} .warn-list{margin:6px 0 0;padding-left:18px;color:var(--warn);font-size:.86rem;} .empty{padding:18px;border:1px dashed var(--line-strong);border-radius:14px;color:var(--muted);background:#fbfaf7;} .form-grid{display:grid;grid-template-columns:1.1fr .9fr;gap:14px;align-items:start;}@media (max-width:900px){.form-grid{grid-template-columns:1fr;}} .help{font-size:.88rem;color:var(--muted);margin-top:6px;} .topline{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin-bottom:12px;} .sr-only{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0;} </style></head><body>");
        builder.AppendLine("<a class=\"skip-link\" href=\"#main-content\">Skip to content</a><main id=\"main-content\">");
        builder.AppendLine("<header class=\"hero\">\n<section class=\"hero-card\" aria-labelledby=\"page-title\">\n<div class=\"topline\"><span class=\"status-pill " + (dashboard.Snapshot.State.BootstrapCompleted ? "status-ready\">Node ready" : "status-warn\">Bootstrap required") + "</span><span class=\"pill\">NDJSON events</span><span class=\"pill\">Self-update enabled</span></div>");
        builder.AppendLine("<h1 id=\"page-title\">SinterNode</h1><p class=\"muted\">Minimal operational view for bootstrap, service discovery, deployments, and node health.</p>");
        builder.AppendLine("<div class=\"summary-grid\">\n<div class=\"stat\"><span class=\"stat-label\">Bootstrap</span><span class=\"stat-value\">" + Encode(bootstrapState) + "</span><span class=\"stat-note\">Node key and prefixes</span></div>");
        builder.AppendLine("<div class=\"stat\"><span class=\"stat-label\">Prefixes</span><span class=\"stat-value\">" + dashboard.Snapshot.State.ServicePrefixes.Length + "</span><span class=\"stat-note\">Discovery filters configured</span></div>");
        builder.AppendLine("<div class=\"stat\"><span class=\"stat-label\">Services</span><span class=\"stat-value\">" + dashboard.Services.Count + "</span><span class=\"stat-note\">" + servicesManagedCount + " managed, " + servicesWithOverrides + " with overrides</span></div>");
        builder.AppendLine("<div class=\"stat\"><span class=\"stat-label\">Apps</span><span class=\"stat-value\">" + dashboard.ManagedApplications.Count + "</span><span class=\"stat-note\">Managed deployment records</span></div>\n</div>\n</section>");
        builder.AppendLine("<section aria-labelledby=\"identity-title\"><div class=\"section-head\"><div><h2 id=\"identity-title\">Node identity</h2><p class=\"muted compact\">Stable facts and runtime values you should be able to scan in a few seconds.</p></div></div><dl class=\"meta\">");
        builder.AppendLine($"<div><dt>Hostname</dt><dd>{Encode(dashboard.Hostname)}</dd></div>");
        builder.AppendLine($"<div><dt>Listen URLs</dt><dd>{Encode(listenUrls)}</dd></div>");
        builder.AppendLine($"<div><dt>OS</dt><dd>{Encode(dashboard.OsDescription)}</dd></div>");
        builder.AppendLine($"<div><dt>Runtime</dt><dd>{Encode(dashboard.FrameworkDescription)}</dd></div>");
        builder.AppendLine($"<div><dt>Architecture</dt><dd>{Encode(dashboard.ProcessArchitecture)}</dd></div>");
        builder.AppendLine($"<div><dt>Version</dt><dd>{Encode(dashboard.Version)}</dd></div>");
        builder.AppendLine($"<div><dt>Uptime</dt><dd>{Encode(dashboard.Uptime)}</dd></div>");
        builder.AppendLine($"<div><dt>Node ID</dt><dd class=\"mono\">{Encode(dashboard.Snapshot.State.NodeId.ToString())}</dd></div>");
        builder.AppendLine("</dl></section></header>");

        builder.AppendLine("<section aria-labelledby=\"bootstrap-title\"><div class=\"section-head\"><div><h2 id=\"bootstrap-title\">Bootstrap and filters</h2><p class=\"muted compact\">Set the service prefixes SinterNode should watch. These filters drive the service inventory shown below.</p></div><span class=\"status-pill " + (dashboard.Snapshot.ShowApiKey ? "status-warn\">Save the key now" : "status-ready\">Key already issued") + "</span></div>");
        if (dashboard.Snapshot.ShowApiKey)
        {
            builder.AppendLine("<p>The node key is generated once on first boot. Save it before leaving this page. Later changes require the same key.</p>");
            builder.AppendLine($"<div class=\"key-box\"><strong>X-Sinter-Key</strong><br><code>{Encode(dashboard.Snapshot.ApiKey)}</code></div>");
        }
        else
        {
            builder.AppendLine("<p>The bootstrap key is hidden after setup. Enter it below when you need to change the prefix list.</p>");
        }

        builder.AppendLine("<form method=\"post\" action=\"/ui/configure\" class=\"form-grid\"><div><label for=\"prefixes\"><strong>Service prefixes</strong></label><div class=\"help\">One per line or comma-separated. Example: <span class=\"mono\">HomeLab</span></div><textarea id=\"prefixes\" name=\"prefixes\" rows=\"7\" aria-describedby=\"prefix-help\">" + Encode(prefixes) + "</textarea><div id=\"prefix-help\" class=\"help\">Only services in <span class=\"mono\">/etc/systemd/system</span> whose names start with these prefixes will be shown.</div></div><div class=\"stack\"><div><label for=\"apiKey\"><strong>API key</strong></label><input id=\"apiKey\" name=\"apiKey\" type=\"password\" placeholder=\"Required after bootstrap\" autocomplete=\"off\"></div><div class=\"empty compact\">Protected API header: <span class=\"mono\">X-Sinter-Key</span><br>Event stream format: <span class=\"mono\">application/x-ndjson</span></div><div><button type=\"submit\">Save prefixes</button></div></div></form>");
        builder.AppendLine("</section>");

        builder.AppendLine("<section aria-labelledby=\"prefix-title\"><div class=\"section-head\"><div><h2 id=\"prefix-title\">Configured prefixes</h2><p class=\"muted compact\">Fast confirmation of what the node will discover.</p></div></div>");
        if (dashboard.Snapshot.State.ServicePrefixes.Length == 0)
        {
            builder.AppendLine("<div class=\"empty\">No prefixes configured yet.</div>");
        }
        else
        {
            foreach (var prefix in dashboard.Snapshot.State.ServicePrefixes)
            {
                builder.AppendLine($"<span class=\"pill\">{Encode(prefix)}</span>");
            }
        }

        builder.AppendLine("</section>");
        builder.AppendLine("<section aria-labelledby=\"services-title\"><div class=\"section-head\"><div><h2 id=\"services-title\">Discovered services</h2><p class=\"muted compact\">systemd units matched by the configured prefixes.</p></div></div><div class=\"table-wrap\"><table><caption class=\"sr-only\">Discovered services</caption><thead><tr><th scope=\"col\">Service</th><th scope=\"col\">Description</th><th scope=\"col\">State</th><th scope=\"col\">Override</th></tr></thead><tbody>");
        if (dashboard.Services.Count == 0)
        {
            builder.AppendLine("<tr><td colspan=\"4\" class=\"muted\">No matching services found in /etc/systemd/system.</td></tr>");
        }
        else
        {
            foreach (var service in dashboard.Services)
            {
                builder.AppendLine("<tr><td><strong>" + Encode(service.Name) + "</strong><div class=\"path\">" + Encode(service.UnitPath) + "</div></td><td>" + Encode(string.IsNullOrWhiteSpace(service.Description) ? "No description" : service.Description) + "</td><td><span class=\"status-pill " + (service.IsManagedByNode ? "status-ready\">Managed by node" : "status-warn\">Observed only") + "</span></td><td>" + (service.HasOverride ? "<span class=\"status-pill status-ready\">Present</span>" : "<span class=\"muted\">None</span>") + RenderWarnings(service.OverrideWarnings) + "</td></tr>");
            }
        }

        builder.AppendLine("</tbody></table></div></section>");
        builder.AppendLine("<section aria-labelledby=\"apps-title\"><div class=\"section-head\"><div><h2 id=\"apps-title\">Managed applications</h2><p class=\"muted compact\">Deployment metadata prepared for future SinterServer inventory and operations views.</p></div></div><div class=\"table-wrap\"><table><caption class=\"sr-only\">Managed applications</caption><thead><tr><th scope=\"col\">Application</th><th scope=\"col\">Repository</th><th scope=\"col\">Service</th><th scope=\"col\">Releases</th><th scope=\"col\">Current release</th><th scope=\"col\">Last deploy</th></tr></thead><tbody>");
        if (dashboard.ManagedApplications.Count == 0)
        {
            builder.AppendLine("<tr><td colspan=\"6\" class=\"muted\">No managed applications have been deployed yet.</td></tr>");
        }
        else
        {
            foreach (var app in dashboard.ManagedApplications)
            {
                builder.AppendLine("<tr><td><strong>" + Encode(app.AppName) + "</strong><div class=\"path\">" + Encode(app.AppRoot ?? string.Empty) + "</div></td><td>" + Encode(app.RepoUrl) + "</td><td>" + Encode(app.ServiceName) + "</td><td>" + app.ReleaseCount + "</td><td><div class=\"compact\">" + Encode(ShortenPath(app.CurrentRelease)) + "</div><div class=\"path\">" + Encode(app.CurrentRelease ?? string.Empty) + "</div></td><td>" + Encode(app.LastDeploymentUtc?.ToString("u") ?? "Never") + "</td></tr>");
            }
        }

        builder.AppendLine("</tbody></table></div></section>");
        builder.AppendLine("</main></body></html>");
        return builder.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string RenderWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("<ul class=\"warn-list\"> ");
        foreach (var warning in warnings)
        {
            builder.Append("<li>").Append(Encode(warning)).AppendLine("</li>");
        }

        builder.Append("</ul>");
        return builder.ToString();
    }

    private static string ShortenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "None";
        }

        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}