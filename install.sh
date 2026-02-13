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
    cp -r . "$INSTALL_DIR/source"
else
    echo ">>> Cloning Agent from Public Repo..."
    # Clone to a temporary directory first to avoid conflicts if directory exists but is empty/partial
    TEMP_DIR=$(mktemp -d)
    git clone https://github.com/Jeffe747/LinuxAgent.git "$TEMP_DIR/LinuxAgent"
    
    # Move source to install dir
    rm -rf "$INSTALL_DIR/source"
    mkdir -p "$INSTALL_DIR/source"
    cp -r "$TEMP_DIR/LinuxAgent/"* "$INSTALL_DIR/source/"
    rm -rf "$TEMP_DIR"
fi

# 4. Generate Key
echo ">>> Generating API Key..."
API_KEY=$(openssl rand -hex 32)
echo "$API_KEY" > /etc/linux-agent/client_secret
chmod 600 /etc/linux-agent/client_secret

# 5. Create Service
echo ">>> Creating Systemd Service..."
cat > /etc/systemd/system/$SERVICE_NAME.service <<EOF
[Unit]
Description=Linux Agent Service
After=network.target

[Service]
WorkingDirectory=$INSTALL_DIR
ExecStart=/root/.dotnet/dotnet $INSTALL_DIR/LinuxAgent.dll
Restart=always
User=root
Environment=ASPNETCORE_URLS=http://*:5000

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable $SERVICE_NAME

echo "============================================"
echo "   INSTALLATION COMPLETE"
echo "============================================"
echo "Agent Address: http://$(hostname -I | awk '{print $1}'):5000"
echo "API KEY:       $API_KEY"
echo "============================================"
echo "Save this key! You will need it to control the agent."
echo "============================================"
