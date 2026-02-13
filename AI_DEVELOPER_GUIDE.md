# AI Developer Guide (REQUIRED READING)
> **STATUS: ACTIVE | LAST UPDATED: 2026-02-13**
> **CRITICAL**: This file MUST be kept up-to-date. If you modify architecture, workflows, or key components, you MUST update this document.

## 1. Project Overview
**Linux Manager Agent** is a C# .NET 10.0 agent designed to run on Linux servers for managing application deployments and system tasks. It exposes a REST API on port 5000.

**Core Philosophy**:
- **Atomic Deployments**: Never overwrite live files directly. Use symlinks.
- **Zero-Downtime**: Switch versions instantly.
- **Safety First**: Auto-rollback on failure.
- **Minimal Dependencies**: Rely on standard Linux tools (`systemd`, `git`, `ufw`, `journalctl`).

## 2. Key Architecture

### Directory Structure
```
/opt/linux-agent/
├── LinuxAgent.dll       # The agent binary
├── apps/
│   └── {AppName}/
│       ├── repo/        # Persistent Git clone (mirror/bare-like usage)
│       ├── releases/    # Timestamped build folders (e.g., 20260213-120000)
│       └── current ->   # Symlink to active release in releases/
└── client_secret        # API Key file
```

### Services
- **DeploymentService**: Handles git operations, `dotnet publish`, atomic symlink switching (`ln -sfn`), and rollback.
- **CommandRunner**: Executes shell commands and streams output via `Channel<string>` for real-time API feedback.
- **Program.cs**: Entry point. Configures Kestrel, Middleware (Auth, Proxy), and minimal API endpoints.

## 3. Workflows

### Deployment Flow in `DeploymentService.cs`
1.  **Prepare**: Create timestamped folder in `releases/`.
2.  **Build**: `git pull` & `dotnet publish` into the new folder.
3.  **Switch**: Update `current` symlink to new build.
4.  **Restart**: `systemctl restart {app}.service`.
5.  **Verify**: Check exit code.
    -   *Success*: Delete old builds (keep last 5).
    -   *Failure*: Revert symlink to previous path & restart.

### Dashboard
-   **Frontend**: `wwwroot/index.html` (No framework, Vanilla JS).
-   **API**: `GET /api/dashboard` (Public, no auth).
-   **Style**: Discord-like dark theme (`style.css`).

## 4. Environment & Proxies
The agent runs behind YARP/Cloudflare.
-   **Port**: 5000 (HTTP).
-   **Middleware**: `UseForwardedHeaders` is configured to trust **ALL** proxies (`KnownNetworks.Clear()`).
-   **Auth**: `ApiKeyAuthMiddleware` protects `/api/*` logic endpoints, but excludes `/api/dashboard`, `/api/status`, and `/health`.

## 5. Development Rules for AI Agents
1.  **Maintain `task.md`**: Always track your micro-tasks.
2.  **Atomic Edits**: When refactoring critical services like `DeploymentService`, verify with a `dryRun` or test build.
3.  **Update Artifacts**: Keep `walkthrough.md`, `README.md`, and **THIS FILE** updated.
4.  **Security**: Never expose the API key in logs or dashboard.
5.  **Backward Compatibility**: The agent auto-updates via script. Ensure `update.sh` remains functional.

## 6. Common Commands
-   **Run Local**: `dotnet run --urls=http://localhost:5000`
-   **Build**: `dotnet build`
-   **Test Deployment**: Use the `.agent/workflows/deploy.md` guide.
