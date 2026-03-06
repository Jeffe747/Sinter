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
RETAINED_RELEASES="5"
DATABASE_PATH="${STATE_ROOT}/data/sinter-server.db"

REPO_URL="${DEFAULT_REPO_URL}"
BRANCH="${DEFAULT_BRANCH}"
SOURCE_DIR=""

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
Usage: update.sh [--repo-url <url>] [--branch <branch>] [--source-dir <path>]

Publishes a new SinterServer release, switches the current symlink, restarts the
service, and rolls back if the restarted service does not become active.
EOF
}

require_root() {
    if [[ "${EUID}" -ne 0 ]]; then
        echo "This update script must run as root." >&2
        exit 1
    fi
}

load_environment() {
    if [[ -f "${ENV_FILE}" ]]; then
        # shellcheck disable=SC1090
        source "${ENV_FILE}"
        REPO_URL="${SINTER_REPO_URL:-${REPO_URL}}"
        BRANCH="${SINTER_BRANCH:-${BRANCH}}"
        PROJECT_PATH="${SINTER_PROJECT_PATH:-${PROJECT_PATH}}"
        DATABASE_PATH="${SINTERSERVER__DATABASEPATH:-${DATABASE_PATH}}"
    fi
}

prepare_paths() {
    mkdir -p "${STATE_ROOT}/releases" "${STATE_ROOT}/repo-cache" "${INSTALL_ROOT}" "$(dirname "${DATABASE_PATH}")" "${CONFIG_ROOT}"
}

migrate_database_if_needed() {
    if [[ -f "${DATABASE_PATH}" ]]; then
        return
    fi

    local legacy_path
    for legacy_path in "${INSTALL_ROOT}/data/sinter-server.db" "${INSTALL_ROOT}/current/data/sinter-server.db"; do
        if [[ -f "${legacy_path}" ]]; then
            cp -a "${legacy_path}" "${DATABASE_PATH}"
            echo ">>> Migrated database to ${DATABASE_PATH}."
            return
        fi
    done
}

write_environment() {
    local current_urls
    current_urls="http://0.0.0.0:5656"

    if [[ -f "${ENV_FILE}" ]]; then
        current_urls="$(awk -F= '/^ASPNETCORE_URLS=/{sub(/^[^=]*=/, ""); print; exit}' "${ENV_FILE}")"
        current_urls="${current_urls:-http://0.0.0.0:5656}"
    fi

    cat > "${ENV_FILE}" <<EOF
ASPNETCORE_URLS=${current_urls}
DOTNET_ENVIRONMENT=Production
SINTER_PORT=${SINTER_PORT:-}
SINTER_REPO_URL=${REPO_URL}
SINTER_BRANCH=${BRANCH}
SINTER_PROJECT_PATH=${PROJECT_PATH}
SINTERSERVER__DATABASEPATH=${DATABASE_PATH}
EOF
    chmod 600 "${ENV_FILE}"
}

write_systemd_unit() {
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

prepare_repository() {
    local repo_cache
    repo_cache="${STATE_ROOT}/repo-cache"

    resolve_branch

    if [[ -n "${SOURCE_DIR}" ]]; then
        REPO_WORKDIR="${SOURCE_DIR}"
        return
    fi

    if [[ -d "${repo_cache}/.git" ]]; then
        git -C "${repo_cache}" fetch origin
        git -C "${repo_cache}" reset --hard "origin/${BRANCH}"
    else
        rm -rf "${repo_cache}"
        git clone --branch "${BRANCH}" "${REPO_URL}" "${repo_cache}"
    fi

    REPO_WORKDIR="${repo_cache}"
}

publish_release() {
    TIMESTAMP="$(date -u +%Y%m%d-%H%M%S)"
    RELEASE_ROOT="${STATE_ROOT}/releases/${TIMESTAMP}"
    PUBLISH_ROOT="${RELEASE_ROOT}/publish"
    mkdir -p "${RELEASE_ROOT}"
    "${DOTNET_PATH}" publish "${REPO_WORKDIR}/${PROJECT_PATH}" -c Release -o "${PUBLISH_ROOT}"
    chmod +x "${PUBLISH_ROOT}/update.sh" || true
}

activate_release() {
    CURRENT_LINK="${INSTALL_ROOT}/current"
    PREVIOUS_TARGET=""

    if [[ -e "${CURRENT_LINK}" || -L "${CURRENT_LINK}" ]]; then
        PREVIOUS_TARGET="$(readlink -f "${CURRENT_LINK}")"
    fi

    ln -sfn "${PUBLISH_ROOT}" "${CURRENT_LINK}"
    write_systemd_unit
    systemctl restart "${SERVICE_NAME}"

    if ! systemctl is-active --quiet "${SERVICE_NAME}"; then
        echo ">>> Restart failed, rolling back..." >&2
        if [[ -n "${PREVIOUS_TARGET}" && -d "${PREVIOUS_TARGET}" ]]; then
            ln -sfn "${PREVIOUS_TARGET}" "${CURRENT_LINK}"
            write_systemd_unit
            systemctl restart "${SERVICE_NAME}" || true
        fi
        exit 1
    fi
}

cleanup_old_releases() {
    local releases_root
    releases_root="${STATE_ROOT}/releases"
    mapfile -t releases < <(find "${releases_root}" -mindepth 1 -maxdepth 1 -type d | sort -r)

    if [[ "${#releases[@]}" -le "${RETAINED_RELEASES}" ]]; then
        return
    fi

    local index=0
    local release
    for release in "${releases[@]}"; do
        index=$((index + 1))
        if [[ "${index}" -le "${RETAINED_RELEASES}" ]]; then
            continue
        fi

        if [[ "${release}" == "${RELEASE_ROOT}" ]]; then
            continue
        fi

        rm -rf "${release}"
    done
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --repo-url)
            REPO_URL="$2"
            shift 2
            ;;
        --branch)
            BRANCH="$2"
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

require_root
load_environment
prepare_paths
migrate_database_if_needed
write_environment
prepare_repository
publish_release
activate_release
cleanup_old_releases

echo ">>> SinterServer update complete."