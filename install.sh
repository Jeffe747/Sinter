#!/bin/bash
set -e

# Arguments: None required for public repo

TOKEN=""
INSTALL_DIR="/opt/linux-agent"
SERVICE_NAME="linux-agent"

echo ">>> Starting Linux Agent Installation..."

# 1. Install Dependencies
echo ">>> Installing dependencies..."
if ! command -v dotnet &> /dev/null; then
    echo "Installing .NET SDK..."
    apt-get update
    apt-get install -y dotnet-sdk-8.0 # Fallback/Prereq (Ubuntu repos might not have 10 yet, using 8 for now or script from MS)
    # Actually for .NET 10 we might need the install script.
    wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
    chmod +x ./dotnet-install.sh
    ./dotnet-install.sh --channel 10.0
    # Add to path for root
    export PATH="$PATH:/root/.dotnet"
fi

apt-get install -y git ufw

# 2. Setup Directory
echo ">>> Setting up directories..."
mkdir -p "$INSTALL_DIR"
mkdir -p "/etc/linux-agent"

# 3. Clone/Copy Agent
# If we are running this from the repo itself, we just copy. 
# If running via curl, we might need to clone.

if [ -d "./LinuxAgent" ]; then
    echo ">>> Found local source, using it..."
    SOURCE_DIR="."
else
    echo ">>> Cloning Agent from Public Repo..."
    TEMP_DIR=$(mktemp -d)
    git clone https://github.com/Jeffe747/LinuxAgent.git "$TEMP_DIR/LinuxAgent"
    SOURCE_DIR="$TEMP_DIR/LinuxAgent"
fi

# Stop service if running to allow update
echo ">>> Stopping existing service (if any)..."
systemctl stop $SERVICE_NAME || true

echo ">>> Building and Publishing Agent..."
# Publish directly to install dir
dotnet publish "$SOURCE_DIR/LinuxAgent/LinuxAgent.csproj" -c Release -o "$INSTALL_DIR"

if [ -n "$TEMP_DIR" ]; then
    rm -rf "$TEMP_DIR"
fi

# 4. Generate Key
echo ">>> Generating API Key..."
if [ ! -f "/etc/linux-agent/client_secret" ]; then
    API_KEY=$(openssl rand -hex 32)
    echo "$API_KEY" > /etc/linux-agent/client_secret
    chmod 600 /etc/linux-agent/client_secret
else
    echo "Key already exists, keeping it."
    API_KEY=$(cat /etc/linux-agent/client_secret)
fi

# 5. Create Service
echo ">>> Creating Systemd Service..."
cat > /etc/systemd/system/$SERVICE_NAME.service <<EOF
[Unit]
Description=Linux Agent Service
After=network.target

[Service]
WorkingDirectory=$INSTALL_DIR
ExecStart=/usr/bin/dotnet $INSTALL_DIR/LinuxAgent.dll
Restart=always
User=root
Environment=ASPNETCORE_URLS=http://*:5000
# Ensure dotnet is in path or use full path above.
# We used /root/.dotnet/dotnet before, but dotnet-install.sh might put it there.
# Let's try to find dotnet location dynamically or use the one we installed.
EOF

# Update ExecStart with actual dotnet path
DOTNET_PATH=$(which dotnet || echo "/root/.dotnet/dotnet")
sed -i "s|ExecStart=.*|ExecStart=$DOTNET_PATH $INSTALL_DIR/LinuxAgent.dll|" /etc/systemd/system/$SERVICE_NAME.service

systemctl daemon-reload
systemctl enable $SERVICE_NAME
systemctl start $SERVICE_NAME

echo "============================================"
echo "   INSTALLATION COMPLETE"
echo "============================================"
echo "Agent Address: http://$(hostname -I | awk '{print $1}'):5000"
echo "API KEY:       $API_KEY"
echo "============================================"
echo "Save this key! You will need it to control the agent."
echo "============================================"
