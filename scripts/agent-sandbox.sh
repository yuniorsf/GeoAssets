#!/usr/bin/env bash
# ==============================================================================
# Multi-Agent Git Worktree Sandbox Manager (Language-Agnostic)
# ==============================================================================
# Safely creates, initializes, lists, and destroys isolated Git Worktrees
# under ../worktrees/<task-id> to guarantee zero contamination of protected
# branches (`main`, `master`) and deterministic isolation for concurrent agents
# (Google Antigravity, Claude Code, GitHub Copilot).
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
DEFAULT_WORKTREES_ROOT="$(cd "${REPO_ROOT}/.." && pwd)/worktrees"
if [[ -z "${WORKTREES_ROOT:-}" ]]; then
    if mkdir -p "${DEFAULT_WORKTREES_ROOT}" 2>/dev/null; then
        WORKTREES_ROOT="${DEFAULT_WORKTREES_ROOT}"
    else
        WORKTREES_ROOT="${REPO_ROOT}/.claude/worktrees"
    fi
fi
HARNESS_ENV="${REPO_ROOT}/.harness/harness.env"

if [[ -f "${HARNESS_ENV}" ]]; then
    # shellcheck disable=SC1090
    source "${HARNESS_ENV}"
fi

PROJECT_NAME="${PROJECT_NAME:-<app>}"
DEFAULT_BASE_REF="${INTEGRATION_BRANCH:-origin/develop}"
MERGE_TARGET="${MERGE_TARGET_BRANCH:-develop}"
read -r -a PROTECTED_ARRAY <<< "${PROTECTED_BRANCHES:-main master}"
ALL_PROTECTED=("${PROTECTED_ARRAY[@]}" "${MERGE_TARGET}")

log_info()  { printf "\033[1;34m[SANDBOX:INFO]\033[0m  %s\n" "$*"; }
log_ok()    { printf "\033[1;32m[SANDBOX:OK]\033[0m    %s\n" "$*"; }
log_warn()  { printf "\033[1;33m[SANDBOX:WARN]\033[0m  %s\n" "$*" >&2; }
log_error() { printf "\033[1;31m[SANDBOX:ERROR]\033[0m %s\n" "$*" >&2; }

usage() {
    cat <<EOF
Usage: $(basename "$0") <command> [arguments]

Commands:
  create <task-id> [base-ref]   Create and initialize an isolated worktree at ../worktrees/<task-id>
                                (defaults to branching from ${DEFAULT_BASE_REF}; main/master forbidden).
  init <task-id>                Run configured INIT_CMD from .harness/harness.env inside the worktree.
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

    for protected in "${ALL_PROTECTED[@]}"; do
        if [[ "${task_id}" == "${protected}" || "${task_id}" == "origin/${protected}" ]]; then
            log_error "Task ID '${task_id}' collides with protected branch '${protected}'. Operation rejected."
            exit 2
        fi
    done
}

validate_base_ref() {
    local base_ref="${1}"
    for protected in "${PROTECTED_ARRAY[@]}"; do
        if [[ "${base_ref}" == "${protected}" || "${base_ref}" == "origin/${protected}" ]]; then
            log_error "Branching from '${base_ref}' is strictly forbidden by Branching Policy. Always branch from '${DEFAULT_BASE_REF}'."
            exit 2
        fi
    done
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

    log_info "Fetching latest '${MERGE_TARGET}' in ${REPO_ROOT}..."
    git -C "${REPO_ROOT}" fetch origin "${MERGE_TARGET}" --quiet 2>/dev/null || log_warn "Offline or no 'origin' remote yet; using local ref."

    if ! git -C "${REPO_ROOT}" rev-parse --verify "${base_ref}^{commit}" >/dev/null 2>&1; then
        if git -C "${REPO_ROOT}" rev-parse --verify "${MERGE_TARGET}^{commit}" >/dev/null 2>&1; then
            log_warn "Ref '${base_ref}' not found locally; falling back to '${MERGE_TARGET}'."
            base_ref="${MERGE_TARGET}"
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
    for protected in "${PROTECTED_ARRAY[@]}"; do
        if [[ "${current_branch}" == "${protected}" ]]; then
            log_error "Worktree '${worktree_path}' is checked out on protected branch '${current_branch}'! Aborting."
            exit 2
        fi
    done

    if [[ -n "${INIT_CMD:-}" ]]; then
        log_info "Running worktree INIT_CMD ('${INIT_CMD}') in '${worktree_path}'..."
        (
            cd "${worktree_path}"
            eval "${INIT_CMD}"
        )
    fi
    log_ok "Initialized worktree '${task_id}' on branch '${current_branch}'."
}

cmd_list() {
    log_info "Active Git worktrees for ${PROJECT_NAME}:"
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
                    local wt_name branch dirty_count
                    wt_name="$(basename "${wt}")"
                    branch="$(git -C "${wt}" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")"
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

    for protected in "${ALL_PROTECTED[@]}"; do
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
