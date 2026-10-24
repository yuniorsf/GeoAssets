#!/usr/bin/env bash
# ==============================================================================
# GeoAssets — Multi-Agent Git Worktree Sandbox Manager
# ==============================================================================
# Safely creates, initializes, lists, and destroys isolated Git Worktrees
# under ../worktrees/<task-id> to guarantee zero contamination of `main` and
# deterministic branch isolation for concurrent agents (Antigravity, Claude Code,
# GitHub Copilot).
#
# Usage:
#   ./scripts/agent-sandbox.sh create <task-id> [base-ref]
#   ./scripts/agent-sandbox.sh init <task-id>
#   ./scripts/agent-sandbox.sh list
#   ./scripts/agent-sandbox.sh destroy <task-id> [--force]
#   ./scripts/agent-sandbox.sh prune
# ==============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKTREES_ROOT="$(cd "${REPO_ROOT}/.." && pwd)/worktrees"
DEFAULT_BASE_REF="origin/develop"
PROTECTED_BRANCHES=("main" "master" "develop")

log_info()  { printf "\033[1;34m[SANDBOX:INFO]\033[0m  %s\n" "$*"; }
log_ok()    { printf "\033[1;32m[SANDBOX:OK]\033[0m    %s\n" "$*"; }
log_warn()  { printf "\033[1;33m[SANDBOX:WARN]\033[0m  %s\n" "$*" >&2; }
log_error() { printf "\033[1;31m[SANDBOX:ERROR]\033[0m %s\n" "$*" >&2; }

usage() {
    cat <<EOF
Usage: $(basename "$0") <command> [arguments]

Commands:
  create <task-id> [base-ref]   Create and initialize an isolated worktree at ../worktrees/<task-id>
                                (defaults to branching from ${DEFAULT_BASE_REF}; 'main' is forbidden).
  init <task-id>                Restore NuGet packages and validate an existing worktree.
  list                          List all active Git worktrees and their status.
  destroy <task-id> [--force]   Remove ../worktrees/<task-id>, prune metadata, and delete its local branch.
  prune                         Prune stale Git worktree administrative files.
EOF
}

validate_task_id() {
    local task_id="${1:-}"
    if [[ -z "${task_id}" ]]; then
        log_error "Missing required <task-id>."
        usage
        exit 2
    fi

    if [[ ! "${task_id}" =~ ^[a-zA-Z0-9._-]+$ ]]; then
        log_error "Invalid <task-id> '${task_id}'. Only alphanumeric characters, '.', '_', and '-' are allowed."
        exit 2
    fi

    for protected in "${PROTECTED_BRANCHES[@]}"; do
        if [[ "${task_id}" == "${protected}" || "${task_id}" == "origin/${protected}" ]]; then
            log_error "Task ID '${task_id}' collides with protected branch '${protected}'. Operation rejected."
            exit 2
        fi
    done
}

validate_base_ref() {
    local base_ref="${1}"
    if [[ "${base_ref}" == "main" || "${base_ref}" == "origin/main" || "${base_ref}" == "master" || "${base_ref}" == "origin/master" ]]; then
        log_error "Branching from '${base_ref}' is strictly forbidden by GeoAssets Branching Policy. Always branch from '${DEFAULT_BASE_REF}'."
        exit 2
    fi
}

normalize_branch_name() {
    local task_id="$1"
    echo "${task_id}" | tr '[:upper:]' '[:lower:]'
}

cmd_create() {
    local task_id="${1:-}"
    local base_ref="${2:-${DEFAULT_BASE_REF}}"

    validate_task_id "${task_id}"
    validate_base_ref "${base_ref}"

    local branch_name
    branch_name="$(normalize_branch_name "${task_id}")"
    local worktree_path="${WORKTREES_ROOT}/${task_id}"

    if [[ -e "${worktree_path}" ]]; then
        log_error "Worktree path '${worktree_path}' already exists. Use 'init' or 'destroy' first."
        exit 1
    fi

    mkdir -p "${WORKTREES_ROOT}"

    log_info "Fetching latest '${DEFAULT_BASE_REF}' in ${REPO_ROOT}..."
    git -C "${REPO_ROOT}" fetch origin develop --quiet || log_warn "Offline or unable to fetch origin/develop; using local ref '${base_ref}'."

    if ! git -C "${REPO_ROOT}" rev-parse --verify "${base_ref}^{commit}" >/dev/null 2>&1; then
        if git -C "${REPO_ROOT}" rev-parse --verify "develop^{commit}" >/dev/null 2>&1; then
            log_warn "Ref '${base_ref}' not found locally; falling back to 'develop'."
            base_ref="develop"
        else
            log_error "Base ref '${base_ref}' could not be resolved."
            exit 1
        fi
    fi

    log_info "Creating isolated worktree at '${worktree_path}' on branch '${branch_name}' (base: '${base_ref}')..."
    if git -C "${REPO_ROOT}" show-ref --verify --quiet "refs/heads/${branch_name}"; then
        git -C "${REPO_ROOT}" worktree add "${worktree_path}" "${branch_name}"
    else
        git -C "${REPO_ROOT}" worktree add -b "${branch_name}" "${worktree_path}" "${base_ref}"
    fi

    cmd_init "${task_id}"
    log_ok "Worktree ready: ${worktree_path} (branch: ${branch_name})"
}

cmd_init() {
    local task_id="${1:-}"
    validate_task_id "${task_id}"

    local worktree_path="${WORKTREES_ROOT}/${task_id}"
    if [[ ! -d "${worktree_path}" ]]; then
        log_error "Worktree directory '${worktree_path}' does not exist."
        exit 1
    fi

    local current_branch
    current_branch="$(git -C "${worktree_path}" rev-parse --abbrev-ref HEAD)"
    if [[ "${current_branch}" == "main" || "${current_branch}" == "master" ]]; then
        log_error "Worktree '${worktree_path}' is checked out on protected branch '${current_branch}'! Aborting."
        exit 2
    fi

    log_info "Initializing .NET dependencies in '${worktree_path}'..."
    (
        cd "${worktree_path}"
        dotnet restore GeoAssets.sln --nologo --verbosity quiet
    )
    log_ok "Initialized worktree '${task_id}' on branch '${current_branch}'."
}

cmd_list() {
    log_info "Active Git worktrees for GeoAssets:"
    git -C "${REPO_ROOT}" worktree list
    echo ""
    if [[ -d "${WORKTREES_ROOT}" ]]; then
        log_info "Sandbox directory (${WORKTREES_ROOT}):"
        shopt -s nullglob
        local entries=("${WORKTREES_ROOT}"/*)
        shopt -u nullglob
        if [[ ${#entries[@]} -eq 0 ]]; then
            echo "  (no active sandboxes in ${WORKTREES_ROOT})"
        else
            for wt in "${entries[@]}"; do
                if [[ -d "${wt}" ]]; then
                    local wt_name
                    wt_name="$(basename "${wt}")"
                    local branch
                    branch="$(git -C "${wt}" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")"
                    local dirty_count
                    dirty_count="$(git -C "${wt}" status --porcelain 2>/dev/null | wc -l | tr -d ' ')"
                    printf "  - %-36s branch: %-32s modified_files: %s\n" "${wt_name}" "${branch}" "${dirty_count}"
                fi
            done
        fi
    fi
}

cmd_destroy() {
    local task_id="${1:-}"
    local force_flag="${2:-}"
    validate_task_id "${task_id}"

    local worktree_path="${WORKTREES_ROOT}/${task_id}"
    local branch_name
    branch_name="$(normalize_branch_name "${task_id}")"

    if [[ -d "${worktree_path}" ]]; then
        branch_name="$(git -C "${worktree_path}" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "${branch_name}")"
        log_info "Removing worktree '${worktree_path}'..."
        if [[ "${force_flag}" == "--force" || "${force_flag}" == "-f" ]]; then
            git -C "${REPO_ROOT}" worktree remove --force "${worktree_path}"
        else
            git -C "${REPO_ROOT}" worktree remove "${worktree_path}"
        fi
    else
        log_warn "Worktree path '${worktree_path}' not found on disk; pruning metadata."
    fi

    git -C "${REPO_ROOT}" worktree prune -v

    for protected in "${PROTECTED_BRANCHES[@]}"; do
        if [[ "${branch_name}" == "${protected}" ]]; then
            log_warn "Skipping deletion of protected branch '${branch_name}'."
            return 0
        fi
    done

    if git -C "${REPO_ROOT}" show-ref --verify --quiet "refs/heads/${branch_name}"; then
        log_info "Deleting local sandbox branch '${branch_name}'..."
        if [[ "${force_flag}" == "--force" || "${force_flag}" == "-f" ]]; then
            git -C "${REPO_ROOT}" branch -D "${branch_name}"
        else
            git -C "${REPO_ROOT}" branch -d "${branch_name}" || log_warn "Branch '${branch_name}' has unmerged commits; re-run with --force to delete."
        fi
    fi

    log_ok "Sandbox '${task_id}' destroyed cleanly."
}

cmd_prune() {
    log_info "Pruning stale worktree metadata..."
    git -C "${REPO_ROOT}" worktree prune -v
    log_ok "Worktree prune complete."
}

main() {
    local subcommand="${1:-}"
    if [[ -z "${subcommand}" ]]; then
        usage
        exit 2
    fi
    shift || true

    case "${subcommand}" in
        create)  cmd_create "$@" ;;
        init)    cmd_init "$@" ;;
        list)    cmd_list ;;
        destroy) cmd_destroy "$@" ;;
        prune)   cmd_prune ;;
        -h|--help|help) usage ;;
        *)
            log_error "Unknown command: '${subcommand}'"
            usage
            exit 2
            ;;
    esac
}

main "$@"

