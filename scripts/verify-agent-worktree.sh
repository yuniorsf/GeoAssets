#!/usr/bin/env bash
# ==============================================================================
# Multi-Agent Deterministic Oracle & Worktree Gatekeeper (Language-Agnostic)
# ==============================================================================
# Reads toolchain commands from `.harness/harness.env` and executes the
# universal three-stage verification gate inside an agent worktree:
#   Stage 1: FORMAT_CHECK_CMD      (Code formatting & style verification)
#   Stage 2: BUILD_AND_LINT_CMD    (Strict compiler, typecheck & static analyzers)
#            + optional STAGE2_POST_CHECK_CMD
#   Stage 3: `scripts/verify-solid-contracts.py` polyglot SOLID check
#            + ARCH_TEST_CMD       (Architecture boundary & domain invariant tests)
#
# Exit Codes:
#   0  — All Oracle checks passed
#   2  — Invalid arguments, missing harness config, or protected branch (`main`)
#   10 — Stage 1 failed (Formatting / style drift -> route to GitHub Copilot)
#   20 — Stage 2 failed (Compiler / typecheck / linter / suppression violation -> route to Claude Code)
#   30 — Stage 3 failed (Architecture / SOLID / invariant failure -> route to Claude Code)
# ==============================================================================

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEFAULT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TARGET_DIR="${1:-$(pwd)}"

if [[ ! -d "${TARGET_DIR}" ]]; then
    printf "\033[1;31m[ORACLE:FATAL]\033[0m Target worktree directory does not exist: %s\n" "${TARGET_DIR}" >&2
    exit 2
fi

TARGET_DIR="$(cd "${TARGET_DIR}" && pwd)"
cd "${TARGET_DIR}" || exit 2

HARNESS_ENV=".harness/harness.env"
if [[ ! -f "${HARNESS_ENV}" ]] && [[ -f "${DEFAULT_ROOT}/.harness/harness.env" ]]; then
    HARNESS_ENV="${DEFAULT_ROOT}/.harness/harness.env"
fi

if [[ ! -f "${HARNESS_ENV}" ]]; then
    printf "\033[1;31m[ORACLE:FATAL]\033[0m Missing '.harness/harness.env' in %s\n" "${TARGET_DIR}" >&2
    exit 2
fi

# shellcheck disable=SC1090
source "${HARNESS_ENV}"

read -r -a PROTECTED_ARRAY <<< "${PROTECTED_BRANCHES:-main master}"
CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")"

if [[ "${GITHUB_ACTIONS:-false}" != "true" ]]; then
    for protected in "${PROTECTED_ARRAY[@]}"; do
        if [[ "${CURRENT_BRANCH}" == "${protected}" ]]; then
            printf "\033[1;31m[ORACLE:FATAL]\033[0m Refusing to run agent verification on protected branch '%s'. Switch to an isolated worktree branch.\n" "${CURRENT_BRANCH}" >&2
            exit 2
        fi
    done
fi

LOG_DIR="$(mktemp -d "${TMPDIR:-/tmp}/multiagent-oracle.XXXXXX")"
trap 'rm -rf "${LOG_DIR}"' EXIT

FORMAT_LOG="${LOG_DIR}/01-format.log"
BUILD_LOG="${LOG_DIR}/02-build-lint.log"
POST_BUILD_LOG="${LOG_DIR}/02b-post-build.log"
ARCH_LOG="${LOG_DIR}/03-arch-solid.log"

print_header() {
    printf "\n\033[1;36m==============================================================================\033[0m\n"
    printf "\033[1;36m[ORACLE]\033[0m %s\n" "$1"
    printf "\033[1;36m==============================================================================\033[0m\n"
}

printf "\033[1;34m[ORACLE:START]\033[0m Worktree: %s | Branch: %s | Stack: %s\n" \
    "${TARGET_DIR}" "${CURRENT_BRANCH}" "${STACK_PROFILE:-generic}"

# ------------------------------------------------------------------------------
# Stage 1: Deterministic Formatting & Code Style Check
# ------------------------------------------------------------------------------
print_header "Stage 1/3: Formatting & Style Check (${FORMAT_CHECK_CMD})"
if ! eval "${FORMAT_CHECK_CMD}" >"${FORMAT_LOG}" 2>&1; then
    printf "\033[1;31m[ORACLE:FAIL]\033[0m Stage 1 failed (Exit Code 10): Code style / formatting drift detected.\n" >&2
    tail -n 50 "${FORMAT_LOG}" >&2
    printf "\n\033[1;33m[SELF-HEAL HINT]\033[0m Run '%s' in the worktree and re-verify.\n" "${FORMAT_FIX_CMD:-FORMAT_FIX_CMD}" >&2
    exit 10
fi
printf "\033[1;32m[ORACLE:PASS]\033[0m Stage 1 passed: Zero code style drift.\n"

# ------------------------------------------------------------------------------
# Stage 2: Strict Build, Typecheck & Static Analysis (Zero Suppressions)
# ------------------------------------------------------------------------------
print_header "Stage 2/3: Build, Typecheck & Static Analyzers (${BUILD_AND_LINT_CMD})"
if ! eval "${BUILD_AND_LINT_CMD}" >"${BUILD_LOG}" 2>&1; then
    printf "\033[1;31m[ORACLE:FAIL]\033[0m Stage 2 failed (Exit Code 20): Compiler, typecheck, or static analyzer violation.\n" >&2
    printf "\n\033[1;33m--- Targeted Diagnostic Summary (Self-Healing Input) ---\033[0m\n" >&2
    tail -n 60 "${BUILD_LOG}" >&2
    printf "\n\033[1;33m[SELF-HEAL HINT]\033[0m Fix all reported diagnostics in source. Do NOT suppress linter or compiler rules.\n" >&2
    exit 20
fi

if [[ -n "${STAGE2_POST_CHECK_CMD:-}" ]]; then
    printf "[ORACLE] Running Stage 2 post-check (${STAGE2_POST_CHECK_CMD})...\n"
    if ! eval "${STAGE2_POST_CHECK_CMD}" >"${POST_BUILD_LOG}" 2>&1; then
        printf "\033[1;31m[ORACLE:FAIL]\033[0m Stage 2 post-check failed (Exit Code 20).\n" >&2
        tail -n 60 "${POST_BUILD_LOG}" >&2
        exit 20
    fi
fi
printf "\033[1;32m[ORACLE:PASS]\033[0m Stage 2 passed: Clean build/typecheck/lint with zero warnings or suppressions.\n"

# ------------------------------------------------------------------------------
# Stage 3: Polyglot SOLID Verifier & Architecture / Domain Invariant Tests
# ------------------------------------------------------------------------------
print_header "Stage 3/3: Polyglot SOLID Contracts & Architecture Tests"
SOLID_SCRIPT="${TARGET_DIR}/scripts/verify-solid-contracts.py"
if [[ ! -f "${SOLID_SCRIPT}" ]] && [[ -f "${DEFAULT_ROOT}/scripts/verify-solid-contracts.py" ]]; then
    SOLID_SCRIPT="${DEFAULT_ROOT}/scripts/verify-solid-contracts.py"
fi

if [[ -f "${SOLID_SCRIPT}" ]]; then
    python3 "${SOLID_SCRIPT}" --target "${TARGET_DIR}" >"${ARCH_LOG}" 2>&1
    SOLID_RC=$?
    cat "${ARCH_LOG}"
    if [[ ${SOLID_RC} -ne 0 ]]; then
        exit "${SOLID_RC}"
    fi
fi

if [[ -n "${ARCH_TEST_CMD:-}" ]]; then
    if ! eval "${ARCH_TEST_CMD}" >"${ARCH_LOG}" 2>&1; then
        printf "\033[1;31m[ORACLE:FAIL]\033[0m Stage 3 failed (Exit Code 30): Architecture or invariant test failure.\n" >&2
        tail -n 60 "${ARCH_LOG}" >&2
        exit 30
    fi
fi
printf "\033[1;32m[ORACLE:PASS]\033[0m Stage 3 passed: All SOLID and architectural invariants verified.\n"

printf "\n\033[1;32m[ORACLE:VERIFIED]\033[0m All 3 deterministic verification gates succeeded (Exit Code 0).\n"
exit 0
