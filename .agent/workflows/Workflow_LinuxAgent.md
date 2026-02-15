# Linux Manager Agent Developer Guide

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

### Deploy

Description: Deploy a .NET app using the Linux Manager Agent

1. Ensure the Linux Agent is running on the target server.
2. Ask the user for the following information if not already provided:
   - **Target Server IP**
   - **API Key** (Check `./client_secret` if available, otherwise ask user)
   - **Repository URL**
   - **App Name**
   - **Branch** (default: `main`)
   - **Git Token** (if repository is private)

3. Construct the deployment payload and execute the request:

   **Dry Run:** To verify build without deploying:
   ```bash
   curl -N -H "X-Agent-Key: <YOUR_KEY>" \
        -H "Content-Type: application/json" \
        -X POST http://<SERVER_IP>:5000/api/deploy \
        -d '{
              "repoUrl": "<REPO_URL>",
              "appName": "<APP_NAME>",
              "branch": "<BRANCH>",
              "token": "<TOKEN>",
              "dryRun": true
            }'
   ```
   
   **Deploy:** To deploy the application:
   ```bash
   # Example command pattern
   curl -N -H "X-Agent-Key: <YOUR_KEY>" \
        -H "Content-Type: application/json" \
        -X POST http://<SERVER_IP>:5000/api/deploy \
        -d '{
              "repoUrl": "<REPO_URL>",
              "appName": "<APP_NAME>",
              "branch": "<BRANCH>",
              "token": "<TOKEN>"
            }'
   ```

   **Monitoring Progress:** The `curl -N` command streams the output (e.g., `[INFO]`, `[SUCCESS]`, `[FAIL]`).
   - Parse this output to inform the user of the progress.
   - Wait for `[SUCCESS]` or `[FAIL]` to determine the outcome.

4. **(Optional) Open Firewall Port:**
   If the application requires a specific port (e.g., 8080), ask the user if they want to open it, then run:

   ```bash
   curl -H "X-Agent-Key: <YOUR_KEY>" \
        -H "Content-Type: application/json" \
        -X POST http://<SERVER_IP>:5000/api/system/open-port \
        -d '{ "port": <PORT>, "protocol": "tcp" }'
   ```

### Self Update

Description: Update the Linux Manager Agent to the latest version

1. To update the agent, simply send a POST request to the `/api/update` endpoint.
2. The agent will pull the latest code from the repository, rebuild itself, and restart the service.

   ```bash
   curl -X POST http://<SERVER_IP>:5000/api/update \
        -H "X-Agent-Key: <YOUR_KEY>"
   ```

3. **Verification**: Wait for a few seconds, then check the status to confirm the new version:

   ```bash
   curl http://<SERVER_IP>:5000/api/status \
        -H "X-Agent-Key: <YOUR_KEY>"
   ```

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
