#!/bin/bash
set -e

# Configuration
INSTALL_DIR="/opt/linux-agent"
SERVICE_NAME="linux-agent"

echo ">>> Starting Self-Update..."

# 1. Pull latest code to a temp repo
TEMP_DIR=$(mktemp -d)
echo ">>> Cloning latest changes to $TEMP_DIR..."
git clone https://github.com/Jeffe747/LinuxAgent.git "$TEMP_DIR/LinuxAgent"

# 2. Rebuild
echo ">>> Rebuilding agent..."
# Ensure dotnet is available
export PATH="$PATH:/usr/local/share/dotnet"
dotnet publish "$TEMP_DIR/LinuxAgent/LinuxAgent/LinuxAgent.csproj" -c Release -o "$INSTALL_DIR"

if [ -n "$TEMP_DIR" ]; then
    rm -rf "$TEMP_DIR"
fi

# 3. Restart Service
echo ">>> Restarting service..."
systemctl restart "$SERVICE_NAME"

echo ">>> Update Complete!"
