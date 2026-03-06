#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="sinter-server"
INSTALL_ROOT="/opt/sinter-server"
CONFIG_ROOT="/etc/sinter-server"
ENV_FILE="${CONFIG_ROOT}/sinter-server.env"
PROJECT_PATH="Sinter/SinterServer/SinterServer.csproj"
SYSTEMD_UNIT_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
DEFAULT_REPO_URL="https://github.com/Jeffe747/Sinter.git"
DEFAULT_BRANCH="master"

PORT=""
BRANCH="${DEFAULT_BRANCH}"
REPO_URL="${DEFAULT_REPO_URL}"
SOURCE_DIR=""
TEMP_DIR=""

cleanup() {
    if [[ -n "${TEMP_DIR}" && -d "${TEMP_DIR}" ]]; then
        rm -rf "${TEMP_DIR}"
    fi
}

require_root() {
    if [[ "${EUID}" -ne 0 ]]; then
        echo "This installer must run as root." >&2
        exit 1
    fi
}

validate_port() {
    [[ "$1" =~ ^[0-9]+$ ]] && (( "$1" >= 1 && "$1" <= 65535 ))
}

ensure_port() {
    if [[ -n "${PORT}" ]]; then
        validate_port "${PORT}" || { echo "Invalid port: ${PORT}" >&2; exit 1; }
        return
    fi

    while true; do
        read -r -p "Enter the port SinterServer should listen on: " PORT </dev/tty
        if validate_port "${PORT}"; then
            break
        fi
        echo "Please enter a valid TCP port between 1 and 65535." >&2
    done
}

install_dependencies() {
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y ca-certificates curl git openssl
    if ! command -v dotnet >/dev/null 2>&1; then
        local script
        script="$(mktemp)"
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${script}"
        chmod +x "${script}"
        "${script}" --channel 10.0 --install-dir /usr/local/share/dotnet
        ln -sf /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet
        rm -f "${script}"
    fi
}

discover_source_dir() {
    if [[ -n "${SOURCE_DIR}" ]]; then
        return
    fi

    if [[ -f "./${PROJECT_PATH}" ]]; then
        SOURCE_DIR="$(pwd)"
        return
    fi

    TEMP_DIR="$(mktemp -d)"
    git clone --branch "${BRANCH}" --depth 1 "${REPO_URL}" "${TEMP_DIR}/repo"
    SOURCE_DIR="${TEMP_DIR}/repo"
}

write_environment() {
    mkdir -p "${CONFIG_ROOT}" "${INSTALL_ROOT}"
    cat > "${ENV_FILE}" <<EOF
ASPNETCORE_URLS=http://0.0.0.0:${PORT}
DOTNET_ENVIRONMENT=Production
EOF
    chmod 600 "${ENV_FILE}"
}

publish_server() {
    dotnet publish "${SOURCE_DIR}/${PROJECT_PATH}" -c Release -o "${INSTALL_ROOT}"
}

write_service() {
    cat > "${SYSTEMD_UNIT_PATH}" <<EOF
[Unit]
Description=SinterServer Service
After=network.target

[Service]
WorkingDirectory=${INSTALL_ROOT}
ExecStart=/usr/local/bin/dotnet ${INSTALL_ROOT}/SinterServer.dll
Restart=always
RestartSec=10
User=root
EnvironmentFile=${ENV_FILE}

[Install]
WantedBy=multi-user.target
EOF
    systemctl daemon-reload
    systemctl enable "${SERVICE_NAME}" >/dev/null 2>&1 || true
    systemctl restart "${SERVICE_NAME}"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --port) PORT="$2"; shift 2 ;;
        --branch) BRANCH="$2"; shift 2 ;;
        --repo-url) REPO_URL="$2"; shift 2 ;;
        --source-dir) SOURCE_DIR="$2"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
done

trap cleanup EXIT
require_root
ensure_port
install_dependencies
discover_source_dir
write_environment
publish_server
write_service

echo "SinterServer installed at http://$(hostname -I | awk '{print $1}'):${PORT}"