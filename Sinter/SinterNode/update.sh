#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="sinter-node"
INSTALL_ROOT="/opt/sinter-node"
STATE_ROOT="/var/lib/sinter-node"
CONFIG_ROOT="/etc/sinter-node"
ENV_FILE="${CONFIG_ROOT}/sinter-node.env"
PROJECT_PATH="Sinter/SinterNode/SinterNode.csproj"
DEFAULT_REPO_URL="https://github.com/Jeffe747/Sinter.git"
DEFAULT_BRANCH="master"
DOTNET_PATH="/usr/local/bin/dotnet"
RETAINED_RELEASES="5"

REPO_URL="${DEFAULT_REPO_URL}"
BRANCH="${DEFAULT_BRANCH}"
SOURCE_DIR=""

usage() {
    cat <<'EOF'
Usage: update.sh [--repo-url <url>] [--branch <branch>] [--source-dir <path>]

Publishes a new SinterNode release, switches the current symlink, restarts the
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
    fi
}

prepare_paths() {
    mkdir -p "${STATE_ROOT}/node/releases" "${STATE_ROOT}/node/repo-cache" "${INSTALL_ROOT}"
}

prepare_repository() {
    local repo_cache
    repo_cache="${STATE_ROOT}/node/repo-cache"

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
    RELEASE_ROOT="${STATE_ROOT}/node/releases/${TIMESTAMP}"
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
    systemctl daemon-reload
    systemctl enable "${SERVICE_NAME}" >/dev/null 2>&1 || true
    systemctl restart "${SERVICE_NAME}"

    if ! systemctl is-active --quiet "${SERVICE_NAME}"; then
        echo ">>> Restart failed, rolling back..." >&2
        if [[ -n "${PREVIOUS_TARGET}" && -d "${PREVIOUS_TARGET}" ]]; then
            ln -sfn "${PREVIOUS_TARGET}" "${CURRENT_LINK}"
            systemctl restart "${SERVICE_NAME}" || true
        fi
        exit 1
    fi
}

cleanup_old_releases() {
    local releases_root
    releases_root="${STATE_ROOT}/node/releases"
    mapfile -t releases < <(find "${releases_root}" -mindepth 1 -maxdepth 1 -type d | sort -r)

    if [[ "${#releases[@]}" -le "${RETAINED_RELEASES}" ]]; then
        return
    fi

    local index=0
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
prepare_repository
publish_release
activate_release
cleanup_old_releases

echo ">>> SinterNode update complete."