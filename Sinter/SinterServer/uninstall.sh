#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="sinter-server"
INSTALL_ROOT="/opt/sinter-server"
STATE_ROOT="/var/lib/sinter-server"
CONFIG_ROOT="/etc/sinter-server"
SYSTEMD_UNIT_PATH="/etc/systemd/system/${SERVICE_NAME}.service"

ASSUME_YES="false"

usage() {
    cat <<'EOF'
Usage: uninstall.sh [--yes]

Stops and removes SinterServer completely, including:
- systemd service unit
- published releases and current symlink
- server configuration
- SQLite database and all server state

Use --yes to skip the confirmation prompt.
EOF
}

require_root() {
    if [[ "${EUID}" -ne 0 ]]; then
        echo "This uninstall script must run as root." >&2
        exit 1
    fi
}

confirm() {
    if [[ "${ASSUME_YES}" == "true" ]]; then
        return
    fi

    local response
    read -r -p "This will permanently remove SinterServer data and configuration. Type 'remove' to continue: " response </dev/tty
    if [[ "${response}" != "remove" ]]; then
        echo "Aborted."
        exit 1
    fi
}

remove_service() {
    if systemctl list-unit-files | grep -q "^${SERVICE_NAME}\.service"; then
        systemctl stop "${SERVICE_NAME}" || true
        systemctl disable "${SERVICE_NAME}" || true
    fi

    rm -f "${SYSTEMD_UNIT_PATH}"
    systemctl daemon-reload
    systemctl reset-failed "${SERVICE_NAME}" || true
}

remove_files() {
    rm -rf "${INSTALL_ROOT}" "${STATE_ROOT}" "${CONFIG_ROOT}"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --yes)
            ASSUME_YES="true"
            shift
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

require_root
confirm
remove_service
remove_files

echo ">>> SinterServer has been removed."