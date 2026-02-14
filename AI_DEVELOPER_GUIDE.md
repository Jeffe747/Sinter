# AI Developer Guide (REQUIRED READING)
> **STATUS: ACTIVE | LAST UPDATED: 2026-02-14**
> **CRITICAL**: Keep this file updated on architecture/workflow changes.

## 1. Overview & Architecture
**Linux Manager Agent** (.NET 10.0) manages deployments/tasks on Linux. 

**Core**: Atomic deployments (symlinks), Zero-downtime, Auto-rollback, Minimal deps.

### Directory Structure (`/opt/linux-agent/`)
- `LinuxAgent.dll`: Agent binary.
- `apps/{AppName}/`:
  - `repo/`: Persistent Git mirror.
  - `releases/YYYYMMDD-HHMMSS/`: Build artifacts.
  - `current ->`: Symlink to active release.
- `client_secret`: API Key.

### Key Components
- **DeploymentService**: Git ops, `dotnet publish`, atomic switch, rollback.
- **CommandRunner**: Shell execution with streamed output.
- **Program.cs**: Kestrel, Middleware (Auth, Proxy), API.

## 2. Workflows

### Deployment
1.  **Prepare**: New timestamped released folder.
2.  **Build**: `git pull` & `dotnet publish`.
3.  **Switch**: Update `current` symlink.
4.  **Restart**: `systemctl restart {app}`.
5.  **Verify**: Check exit code. Success: prune old. Failure: revert & restart.

### Dashboard
-   **Frontend**: `wwwroot/index.html` (Vanilla JS, Dark theme).
-   **API**: `GET /api/dashboard` (Public).

## 3. Environment
-   **Port**: 5000 (HTTP), behind YARP/Cloudflare.
-   **Auth**: `ApiKeyAuthMiddleware` for `/api/*` (except dashboard/status).

## 4. AI Rules
1.  **Maintain `task.md`**: Track micro-tasks.
2.  **Safety**: Dry-run critical refactors.
3.  **Docs**: Update `walkthrough.md`, `README.md`, and this file.
4.  **Security**: No API keys in logs.
5.  **Compat**: Ensure `update.sh` works.

## 5. Commands
-   **Run Local**: `dotnet run --urls=http://localhost:5000`
-   **Build**: `dotnet build`
-   **Test Support**: See `.agent/workflows/deploy.md`.
