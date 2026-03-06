#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="sinter-server"
INSTALL_ROOT="/opt/sinter-server"
STATE_ROOT="/var/lib/sinter-server"
CONFIG_ROOT="/etc/sinter-server"
ENV_FILE="${CONFIG_ROOT}/sinter-server.env"
PROJECT_PATH="Sinter/SinterServer/SinterServer.csproj"
SYSTEMD_UNIT_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
DEFAULT_REPO_URL="https://github.com/Jeffe747/Sinter.git"
DEFAULT_BRANCH="main"
DOTNET_PATH="/usr/local/bin/dotnet"
DATABASE_PATH="${STATE_ROOT}/data/sinter-server.db"

PORT=""
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
    local needs_apt="false"
    for command_name in curl git openssl; do
        if ! command -v "${command_name}" >/dev/null 2>&1; then
            needs_apt="true"
            break
        fi
    done

    if ! command -v dotnet >/dev/null 2>&1; then
        needs_apt="true"
    fi

    if [[ "${needs_apt}" != "true" ]]; then
        echo ">>> Required dependencies already exist. Skipping apt package install."
        return
    fi

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
    resolve_branch
    git clone --branch "${BRANCH}" --depth 1 "${REPO_URL}" "${TEMP_DIR}/repo"
    SOURCE_DIR="${TEMP_DIR}/repo"
}

write_environment() {
    mkdir -p "${CONFIG_ROOT}" "${INSTALL_ROOT}" "${STATE_ROOT}/data" "${STATE_ROOT}/releases" "${STATE_ROOT}/repo-cache"
    cat > "${ENV_FILE}" <<EOF
ASPNETCORE_URLS=http://0.0.0.0:${PORT}
DOTNET_ENVIRONMENT=Production
SINTER_PORT=${PORT}
SINTER_REPO_URL=${REPO_URL}
SINTER_BRANCH=${BRANCH}
SINTER_PROJECT_PATH=${PROJECT_PATH}
SINTERSERVER__DATABASEPATH=${DATABASE_PATH}
EOF
    chmod 600 "${ENV_FILE}"
}

write_service() {
    cat > "${SYSTEMD_UNIT_PATH}" <<EOF
[Unit]
Description=SinterServer Service
After=network.target

[Service]
WorkingDirectory=${INSTALL_ROOT}/current
ExecStart=${DOTNET_PATH} ${INSTALL_ROOT}/current/SinterServer.dll
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
    chmod +x "${SOURCE_DIR}/Sinter/SinterServer/update.sh"
    "${SOURCE_DIR}/Sinter/SinterServer/update.sh" --source-dir "${SOURCE_DIR}" --repo-url "${REPO_URL}" --branch "${BRANCH}"
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
write_service
publish_first_release

echo "SinterServer installed at http://$(hostname -I | awk '{print $1}'):${PORT}"