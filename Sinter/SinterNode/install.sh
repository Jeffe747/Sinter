#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="sinter-node"
INSTALL_ROOT="/opt/sinter-node"
STATE_ROOT="/var/lib/sinter-node"
CONFIG_ROOT="/etc/sinter-node"
ENV_FILE="${CONFIG_ROOT}/sinter-node.env"
SYSTEMD_UNIT_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
PROJECT_PATH="Sinter/SinterNode/SinterNode.csproj"
DEFAULT_REPO_URL="https://github.com/Jeffe747/Sinter.git"
DEFAULT_BRANCH="main"

PORT=""
PORT_WAS_PROVIDED="false"
BRANCH="${DEFAULT_BRANCH}"
REPO_URL="${DEFAULT_REPO_URL}"
SOURCE_DIR=""
TEMP_DIR=""

resolve_branch() {
    if git ls-remote --exit-code --heads "${REPO_URL}" "${BRANCH}" >/dev/null 2>&1; then
        return
    fi

    local requested_branch remote_head candidate
    requested_branch="${BRANCH}"
    remote_head="$(git ls-remote --symref "${REPO_URL}" HEAD 2>/dev/null | awk '/^ref:/ { sub("refs/heads/", "", $2); print $2; exit }')"

    if [[ -n "${remote_head}" ]] && git ls-remote --exit-code --heads "${REPO_URL}" "${remote_head}" >/dev/null 2>&1; then
        BRANCH="${remote_head}"
        echo ">>> Branch '${requested_branch}' not found. Using origin HEAD '${BRANCH}'."
        return
    fi

    for candidate in main master; do
        if git ls-remote --exit-code --heads "${REPO_URL}" "${candidate}" >/dev/null 2>&1; then
            BRANCH="${candidate}"
            echo ">>> Branch '${requested_branch}' not found. Falling back to '${BRANCH}'."
            return
        fi
    done

    echo "Unable to resolve a valid branch for ${REPO_URL}." >&2
    exit 1
}

usage() {
    cat <<'EOF'
Usage: install.sh [--port <port>] [--branch <branch>] [--repo-url <url>] [--source-dir <path>]

Installs SinterNode as a systemd service and publishes the first release.
The port is required: either pass --port or choose it interactively.
EOF
}

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
    if [[ ! "$1" =~ ^[0-9]+$ ]]; then
        return 1
    fi

    if (( "$1" < 1 || "$1" > 65535 )); then
        return 1
    fi

    return 0
}

ensure_port() {
    if [[ "${PORT_WAS_PROVIDED}" == "true" ]]; then
        if ! validate_port "${PORT}"; then
            echo "Invalid port: ${PORT}. Expected a value between 1 and 65535." >&2
            exit 1
        fi
        return
    fi

    if [[ -t 0 || -r /dev/tty ]]; then
        local input_port=""
        while true; do
            read -r -p "Enter the port SinterNode should listen on: " input_port </dev/tty
            if validate_port "${input_port}"; then
                PORT="${input_port}"
                break
            fi

            echo "Please enter a valid TCP port between 1 and 65535." >&2
        done

        return
    fi

    echo "A port must be provided for unattended installs." >&2
    echo "Example: curl -sL \"https://raw.githubusercontent.com/Jeffe747/Sinter/main/Sinter/SinterNode/install.sh\" | sudo bash -s -- --port 5000" >&2
    exit 1
}

install_dependencies() {
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y ca-certificates curl git openssl

    if ! command -v dotnet >/dev/null 2>&1; then
        echo ">>> Installing .NET 10 SDK..."
        local dotnet_script
        dotnet_script="$(mktemp)"
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${dotnet_script}"
        chmod +x "${dotnet_script}"
        "${dotnet_script}" --channel 10.0 --install-dir /usr/local/share/dotnet
        ln -sf /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet
        rm -f "${dotnet_script}"
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
    resolve_branch
    echo ">>> Cloning ${REPO_URL} (${BRANCH})..."
    git clone --branch "${BRANCH}" --depth 1 "${REPO_URL}" "${TEMP_DIR}/repo"
    SOURCE_DIR="${TEMP_DIR}/repo"
}

write_environment_file() {
    mkdir -p "${CONFIG_ROOT}" "${INSTALL_ROOT}" "${STATE_ROOT}/config" "${STATE_ROOT}/apps" "${STATE_ROOT}/node/releases"

    cat > "${ENV_FILE}" <<EOF
ASPNETCORE_URLS=http://0.0.0.0:${PORT}
DOTNET_ENVIRONMENT=Production
SINTER_PORT=${PORT}
SINTER_REPO_URL=${REPO_URL}
SINTER_BRANCH=${BRANCH}
SINTER_PROJECT_PATH=${PROJECT_PATH}
EOF

    chmod 600 "${ENV_FILE}"
}

write_systemd_unit() {
    cat > "${SYSTEMD_UNIT_PATH}" <<EOF
[Unit]
Description=SinterNode Service
After=network.target

[Service]
WorkingDirectory=${INSTALL_ROOT}/current
ExecStart=/usr/local/bin/dotnet ${INSTALL_ROOT}/current/SinterNode.dll
Restart=always
RestartSec=10
User=root
EnvironmentFile=${ENV_FILE}

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable "${SERVICE_NAME}" >/dev/null 2>&1 || true
}

publish_first_release() {
    chmod +x "${SOURCE_DIR}/Sinter/SinterNode/update.sh"
    "${SOURCE_DIR}/Sinter/SinterNode/update.sh" --source-dir "${SOURCE_DIR}" --repo-url "${REPO_URL}" --branch "${BRANCH}"
}

warm_and_print_key() {
    local api_key_path
    api_key_path="${STATE_ROOT}/config/client_secret"

    for _ in $(seq 1 20); do
        if curl -fsS "http://127.0.0.1:${PORT}/health" >/dev/null 2>&1; then
            break
        fi
        sleep 1
    done

    curl -fsS "http://127.0.0.1:${PORT}/" >/dev/null 2>&1 || true

    echo "============================================"
    echo "   SINTER NODE INSTALLATION COMPLETE"
    echo "============================================"
    echo "Node Address: http://$(hostname -I | awk '{print $1}'):${PORT}"
    if [[ -f "${api_key_path}" ]]; then
        echo "API KEY:      $(cat "${api_key_path}")"
    else
        echo "API KEY:      Visit the root page to complete bootstrap and reveal the key."
    fi
    echo "============================================"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --port)
            PORT="$2"
            PORT_WAS_PROVIDED="true"
            shift 2
            ;;
        --branch)
            BRANCH="$2"
            shift 2
            ;;
        --repo-url)
            REPO_URL="$2"
            shift 2
            ;;
        --source-dir)
            SOURCE_DIR="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            usage
            exit 1
            ;;
    esac
done

trap cleanup EXIT

require_root
ensure_port
install_dependencies
discover_source_dir
write_environment_file
write_systemd_unit
publish_first_release
warm_and_print_key