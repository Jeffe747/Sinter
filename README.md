# Linux Agent

Lightweight .NET 10 agent for remote server management. Capabilities: App deployment, package install, firewall ops.
**AI Optimized**: Designed for AI agents to deploy apps via HTTP.

## 📦 Installation

**One-line Install**:
```bash
curl -sL "https://raw.githubusercontent.com/Jeffe747/LinuxAgent/main/install.sh" | sudo bash
```

**Actions**: Installs .NET 10, sets up `linux-agent` service, generates API Key at `/etc/linux-agent/client_secret`.

## 🔑 Security
*   **Auth**: `X-Agent-Key` header required.
*   **Storage**: `/etc/linux-agent/client_secret` (root only).
*   **Network**: Listens on port **5000**. Use VPN/UFW.

## 🚀 Usage

### AI Workflow
See [.agent/workflows/deploy.md](.agent/workflows/deploy.md) for step-by-step AI deployment guide.

### 1. Deploy App
```bash
curl -N -H "X-Agent-Key: <KEY>" -H "Content-Type: application/json" -X POST http://<IP>:5000/api/deploy -d '{
    "repoUrl": "https://github.com/Jeffe747/my-app.git",
    "appName": "my-app-service",
    "branch": "main"
}'
```
*Steps*: Clone -> Publish -> Systemd Service -> Start.

### 2. System Ops
**Install Packages**:
```bash
curl -H "X-Agent-Key: <KEY>" -H "Content-Type: application/json" -X POST http://<IP>:5000/api/system/install-libs -d '{ "packages": ["htop"] }'
```

**Open Port**:
```bash
curl -H "X-Agent-Key: <KEY>" -H "Content-Type: application/json" -X POST http://<IP>:5000/api/system/open-port -d '{ "port": 8080, "protocol": "tcp" }'
```

### 3. Dashboard & Status
-   **Dashboard**: `http://<IP>:5000/` (Public)
-   **Status**: `curl -H "X-Agent-Key: <KEY>" http://<IP>:5000/api/status`

## 🛠 Troubleshooting
*   **Logs**: `journalctl -u linux-agent -f`
*   **Recover Key**: `sudo cat /etc/linux-agent/client_secret`

## 📄 License & Credits
MIT License. Developed by **Antigravity**, supervised by **Jeffe747**.
