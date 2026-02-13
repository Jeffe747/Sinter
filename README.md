# Linux Agent

A lightweight .NET 10 agent for remote management of Linux servers. Capabilities include application deployment (Git -> Publish -> Systemd), package installation, and firewall management.

**Designed for AI Agents**: This tool is optimized to be used by AI agents (like yourself) to easily deploy and run applications on Linux servers via simple HTTP requests.

## 📦 Installation (On Server)

1.  **Direct Install**:
    ```bash
    curl -sL "https://raw.githubusercontent.com/Jeffe747/LinuxAgent/main/install.sh" | sudo bash
    ```

2.  **Manual Install**:
    Copy `install.sh` and the source code to the server, then run:
    ```bash
    sudo ./install.sh
    ```

This will:
*   Install .NET 10 SDK (and prereqs).
*   Setup the `linux-agent` systemd service.
*   Generate a secure API Key at `/etc/linux-agent/client_secret`.
*   Print your **API Key** and **Agent URL**.

## 🔑 Security
*   **Authentication**: All requests require the `X-Agent-Key` header.
*   **Key Storage**: The key is stored in `/etc/linux-agent/client_secret`, readable only by `root`.
*   **Network**: By default, it listens on port **5000**. It is recommended to use this over a VPN or restrict access via UFW/Security Groups.

## 🚀 Usage

### 🤖 AI Agent Workflow
If you are an AI agent, you can find the recommended workflow for deploying applications using this tool in [.agent/workflows/deploy.md](.agent/workflows/deploy.md). This workflow provides a step-by-step guide on how to interact with the agent to deploy .NET applications.

### 1. Deploy an App
Streams the build and deployment logs directly to your terminal.

```bash
curl -N -H "X-Agent-Key: <YOUR_KEY>" \
     -H "Content-Type: application/json" \
     -X POST http://<SERVER_IP>:5000/api/deploy \
     -d '{
           "repoUrl": "https://github.com/Jeffe747/my-app.git",
           "appName": "my-app-service",
           "branch": "main"
         }'
```

The agent will:
1.  Clone the repo to `/opt/linux-agent/apps/my-app-service`.
2.  Run `dotnet publish`.
3.  Create/Update a systemd service named `my-app-service`.
4.  Start the service.

### 2. Install System Packages
```bash
curl -H "X-Agent-Key: <YOUR_KEY>" \
     -H "Content-Type: application/json" \
     -X POST http://<SERVER_IP>:5000/api/system/install-libs \
     -d '{ "packages": ["libgdiplus", "htop"] }'
```

### 3. Open Firewall Port (UFW)
```bash
curl -H "X-Agent-Key: <YOUR_KEY>" \
     -H "Content-Type: application/json" \
     -X POST http://<SERVER_IP>:5000/api/system/open-port \
     -d '{ "port": 8080, "protocol": "tcp" }'
```

### 4. Web Dashboard
Open `http://<SERVER_IP>:5000/` in your browser to view system status and deployments. No API key required for viewing.

### 4. Check Status
```bash
curl -H "X-Agent-Key: <YOUR_KEY>" http://<SERVER_IP>:5000/api/status
```

## 🛠 Troubleshooting
*   **Logs**: View agent logs via `journalctl -u linux-agent -f`.
*   **Key Lost?**: SSH into the server and run `sudo cat /etc/linux-agent/client_secret`.

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## ✨ Acknowledgements
This project is developed by **Antigravity** and supervised by **Icicle**.
