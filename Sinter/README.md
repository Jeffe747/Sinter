# Sinter

Lightweight .NET 10 deployment platform for homelab and Linux fleet management.
**Structure**: `SinterNode` runs on each managed Linux machine. `SinterServer` acts as the central controller.

## 📦 Installation

### SinterNode
**One-line Install**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterNode/install.sh?ts=$(date +%s)" | sudo bash
```

**Actions**: Prompts for the node port, installs .NET 10 if needed, sets up the `sinter-node` service, publishes the first release from `main`, and generates the node API key during bootstrap.

If `curl`, `git`, `openssl`, and `dotnet` are already installed, the installer skips `apt` package installation and goes straight to service setup and publishing.

**Unattended Install**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterNode/install.sh?ts=$(date +%s)" | sudo bash -s -- --port 5000
```

### SinterServer
Central management server for Sinter nodes.
**One-line Install**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterServer/install.sh?ts=$(date +%s)" | sudo bash
```

**Actions**: Prompts for the server port, installs .NET 10 if needed, publishes `SinterServer` from `main`, and registers a `sinter-server` systemd service.

**Unattended Install**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterServer/install.sh?ts=$(date +%s)" | sudo bash -s -- --port 5656
```

**Update Existing Server**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterServer/update.sh?ts=$(date +%s)" | sudo bash
```

**Update Existing Node**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterNode/update.sh?ts=$(date +%s)" | sudo bash
```

**After the first install or update**:
- `sudo /opt/sinter-node/current/update.sh`
- `sudo /opt/sinter-server/current/update.sh`
- Or use the `Update` button in the SinterNode or SinterServer header. Both UIs now show a confirm dialog before handing off to the updater script.

**Fresh Uninstall**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterServer/uninstall.sh?ts=$(date +%s)" | sudo bash
```

**Unattended Fresh Uninstall**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterServer/uninstall.sh?ts=$(date +%s)" | sudo bash -s -- --yes
```

## 🔑 Security
*   **Node Auth**: `X-Sinter-Key` header required for protected node APIs.
*   **Key Storage**: `/var/lib/sinter-node/config/client_secret`.
*   **Bootstrap UI**: Root page shows the generated key once, then becomes the node status/config page.

## 🚀 Usage

### 1. Node Dashboard
-   **Dashboard**: `http://<IP>:5000/`
-   **Health**: `http://<IP>:5000/health`
-   **Status API**: `curl http://<IP>:5000/api/status`

### 2. Deploy App to SinterNode
```bash
curl -N -H "X-Sinter-Key: <KEY>" -H "Content-Type: application/json" \
    -X POST http://<IP>:5000/api/apps/deploy \
    -d '{
        "repoUrl": "https://github.com/Jeffe747/my-app.git",
        "appName": "HomeLab.Api",
        "branch": "main"
    }'
```
*Steps*: Clone/fetch -> publish -> update systemd unit -> restart -> rollback on failure.

### 3. Node Operations
**Restart App**:
```bash
curl -N -H "X-Sinter-Key: <KEY>" -X POST http://<IP>:5000/api/apps/<APP_NAME>/restart
```

**Uninstall App**:
```bash
curl -N -H "X-Sinter-Key: <KEY>" -X DELETE http://<IP>:5000/api/apps/<APP_NAME>
```

**Restart Service**:
```bash
curl -N -H "X-Sinter-Key: <KEY>" -X POST http://<IP>:5000/api/services/<SERVICE_NAME>/restart
```

### 4. systemd File Management
**Read Service Unit**:
```bash
curl -H "X-Sinter-Key: <KEY>" http://<IP>:5000/api/services/<SERVICE_NAME>/unit
```

**Write Override File**:
```bash
curl -H "X-Sinter-Key: <KEY>" -H "Content-Type: application/json" \
    -X PUT http://<IP>:5000/api/services/<SERVICE_NAME>/override \
    -d '{ "content": "[Service]\nEnvironment=ASPNETCORE_ENVIRONMENT=Production\n" }'
```

### 5. Self Update
```bash
curl -N -H "X-Sinter-Key: <KEY>" -H "Content-Type: application/json" \
    -X POST http://<IP>:5000/api/node/self-update \
    -d '{}'
```

The node and server web UIs now expose the same self-update handoff through the header `Update` button.

## 🛠 Troubleshooting
*   **Node Logs**: `journalctl -u sinter-node -f`
*   **Node Service File**: `/etc/systemd/system/sinter-node.service`
*   **Recover Node Key**: `sudo cat /var/lib/sinter-node/config/client_secret`
*   **Re-run Node Update**: `sudo /opt/sinter-node/current/update.sh`
*   **Server Logs**: `journalctl -u sinter-server -f`
*   **Server Service File**: `/etc/systemd/system/sinter-server.service`
*   **Update Server**: `sudo /opt/sinter-server/current/update.sh`
*   **Fresh Remove Server**: `sudo /opt/sinter-server/current/uninstall.sh --yes`

If the installer stops during `apt-get update`, the service file will not be created because service setup happens later in the script.

## 📄 Status
SinterNode is implemented and remains the managed Linux runtime.
SinterServer now has SQLite-backed state, encrypted Git tokens, a static control-plane UI, Linux install/update scripts, and a built-in confirmed self-update action in the header.