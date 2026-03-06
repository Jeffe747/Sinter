# Sinter

Lightweight .NET 10 deployment platform for homelab and Linux fleet management.
**Structure**: `SinterNode` runs on each managed Linux machine. `SinterServer` acts as the central controller.

## 📦 Installation

### SinterNode
**One-line Install**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterNode/install.sh?ts=$(date +%s)" | sudo bash
```

### SinterServer
Central management server for Sinter nodes.
**One-line Install**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterServer/install.sh?ts=$(date +%s)" | sudo bash
```

**After the first install or update**:
- Use the `Update` button in the SinterNode or SinterServer header. Both UIs now show a confirm dialog before handing off to the updater script.

**Fresh Uninstall**:
```bash
curl -H "Cache-Control: no-cache, no-store" -H "Pragma: no-cache" -sL "https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterServer/uninstall.sh?ts=$(date +%s)" | sudo bash
```

## 🔑 Security
*   **Node Auth**: `X-Sinter-Key` header required for protected node APIs.
*   **Key Storage**: `/var/lib/sinter-node/config/client_secret`.
*   **Bootstrap UI**: Root page shows the generated key once, then becomes the node status/config page.

## 🚀 Usage

### Node Dashboard
-   **Dashboard**: `http://<IP>:5000/`
-   **Health**: `http://<IP>:5000/health`
-   **Status API**: `curl http://<IP>:5000/api/status`


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