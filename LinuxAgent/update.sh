#!/bin/bash
set -e

# Configuration
INSTALL_DIR="/opt/linux-agent"
SERVICE_NAME="linux-agent"

echo ">>> Starting Self-Update..."
cd "$INSTALL_DIR"

# 1. Pull latest code
echo ">>> Pulling latest changes..."
git pull

# 2. Rebuild
echo ">>> Rebuilding agent..."
/root/.dotnet/dotnet publish -c Release -o "$INSTALL_DIR"

# 3. Restart Service
echo ">>> Restarting service..."
systemctl restart "$SERVICE_NAME"

echo ">>> Update Complete!"
