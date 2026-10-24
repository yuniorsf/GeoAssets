#!/usr/bin/env bash
# ==============================================================================
# GeoAssets — Deterministic Oracle & Worktree Gatekeeper
# ==============================================================================
# Executes the three-stage verification gate inside an agent worktree:
#   Stage 1: `dotnet format --verify-no-changes`
#   Stage 2: `dotnet build --no-incremental /warnaserror`
#            (Roslyn latest-all, AsyncFixer, Meziantou.Analyzer, BannedApiAnalyzers)
#   Stage 3: `dotnet test --no-build` for architecture tests (NetArchTest)
#
# Exit Codes:
#   0  — All Oracle checks passed
#   2  — Invalid arguments, missing solution, or forbidden `main` branch
#   10 — Stage 1 failed (Code formatting / style drift)
#   20 — Stage 2 failed (Compiler or static analyzer error under /warnaserror)
#   30 — Stage 3 failed (Architecture / invariant test failure)
# ==============================================================================

set -uo pipefail

TARGET_DIR="${1:-$(pwd)}"
SOLUTION_FILE="${GEOASSETS_SOLUTION:-GeoAssets.sln}"
ARCH_TEST_FILTER="${GEOASSETS_ARCH_FILTER:-FullyQualifiedName~Architecture|Category=Architecture}"

if [[ ! -d "${TARGET_DIR}" ]]; then
    printf "\033[1;31m[ORACLE:FATAL]\033[0m Target worktree directory does not exist: %s\n" "${TARGET_DIR}" >&2
    exit 2
fi

cd "${TARGET_DIR}" || exit 2

if [[ ! -f "${SOLUTION_FILE}" ]]; then
    printf "\033[1;31m[ORACLE:FATAL]\033[0m Solution file '%s' not found in %s\n" "${SOLUTION_FILE}" "${TARGET_DIR}" >&2
    exit 2
fi

CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")"
if [[ "${CURRENT_BRANCH}" == "main" || "${CURRENT_BRANCH}" == "master" ]]; then
    printf "\033[1;31m[ORACLE:FATAL]\033[0m Refusing to run agent verification on protected branch '%s'. Switch to an isolated worktree branch.\n" "${CURRENT_BRANCH}" >&2
    exit 2
fi

LOG_DIR="$(mktemp -d "${TMPDIR:-/tmp}/geoassets-oracle.XXXXXX")"
trap 'rm -rf "${LOG_DIR}"' EXIT

FORMAT_LOG="${LOG_DIR}/01-format.log"
BUILD_LOG="${LOG_DIR}/02-build.log"
TEST_LOG="${LOG_DIR}/03-arch-test.log"

print_header() {
    printf "\n\033[1;36m==============================================================================\033[0m\n"
    printf "\033[1;36m[ORACLE] %s\033[0m\n" "$1"
    printf "\033[1;36m==============================================================================\033[0m\n"
}

parse_compiler_diagnostics() {
    local logfile="$1"
    printf "\n\033[1;33m--- Targeted Diagnostic Summary (Self-Healing Input) ---\033[0m\n"
    # Match MSBuild/Roslyn diagnostic lines: path(line,col): error CODE: message [project]
    grep -E "(: error [A-Z]+[0-9]+:)|(: warning [A-Z]+[0-9]+:)" "${logfile}" \
        | sed -E 's/^[[:space:]]+//' \
        | sort -u \
        | head -n 50 || true

    printf "\n\033[1;33m--- Analyzer Breakdown by Rule Family ---\033[0m\n"
    grep -Eo "error (AsyncFixer[0-9]+|MA[0-9]+|RS[0-9]+|CA[0-9]+|CS[0-9]+|IDE[0-9]+)" "${logfile}" \
        | awk '{print $2}' \
        | sort \
        | uniq -c \
        | sort -nr || true
}

parse_test_failures() {
    local logfile="$1"
    printf "\n\033[1;33m--- Targeted Architecture / Test Failure Summary ---\033[0m\n"
    grep -E "(Failed[[:space:]]+)|(Error Message:)|(Stack Trace:)|(Assert\.)" "${logfile}" -A 2 \
        | head -n 60 || cat "${logfile}"
}

printf "\033[1;34m[ORACLE:START]\033[0m Worktree: %s | Branch: %s | Solution: %s\n" "${TARGET_DIR}" "${CURRENT_BRANCH}" "${SOLUTION_FILE}"

# ------------------------------------------------------------------------------
# Stage 1: Deterministic Formatting Check
# ------------------------------------------------------------------------------
print_header "Stage 1/3: Running 'dotnet format --verify-no-changes'"
if ! dotnet format "${SOLUTION_FILE}" --verify-no-changes --verbosity diagnostic >"${FORMAT_LOG}" 2>&1; then
    printf "\033[1;31m[ORACLE:FAIL]\033[0m Stage 1 failed (Exit Code 10): Formatting violations detected.\n" >&2
    grep -E "(error WHITESPACE|error IDE|Formatted code file)" "${FORMAT_LOG}" | head -n 40 || tail -n 40 "${FORMAT_LOG}"
    printf "\n\033[1;33m[SELF-HEAL HINT]\033[0m Run 'dotnet format %s' in the worktree and re-verify.\n" "${SOLUTION_FILE}" >&2
    exit 10
fi
printf "\033[1;32m[ORACLE:PASS]\033[0m Stage 1 passed: Zero formatting drift.\n"

# ------------------------------------------------------------------------------
# Stage 2: Strict Non-Incremental Build & Static Analyzers (/warnaserror)
# ------------------------------------------------------------------------------
print_header "Stage 2/3: Running 'dotnet build --no-incremental /warnaserror'"
if ! dotnet build "${SOLUTION_FILE}" --no-incremental /warnaserror -consoleLoggerParameters:NoSummary >"${BUILD_LOG}" 2>&1; then
    printf "\033[1;31m[ORACLE:FAIL]\033[0m Stage 2 failed (Exit Code 20): Compiler or static analyzer violation.\n" >&2
    parse_compiler_diagnostics "${BUILD_LOG}"
    printf "\n\033[1;33m[SELF-HEAL HINT]\033[0m Fix all reported diagnostics in source. Do NOT suppress rules via #pragma or <NoWarn>.\n" >&2
    exit 20
fi
printf "\033[1;32m[ORACLE:PASS]\033[0m Stage 2 passed: Clean build with zero Roslyn/AsyncFixer/Meziantou/BannedApi warnings.\n"

# ------------------------------------------------------------------------------
# Stage 3: Architecture & Domain Invariant Tests (NetArchTest)
# ------------------------------------------------------------------------------
print_header "Stage 3/3: Running 'dotnet test --no-build' for Architecture Tests"
shopt -s nullglob
ARCH_PROJECTS=(tests/*Architecture*.Tests/*.csproj)
shopt -u nullglob

if [[ ${#ARCH_PROJECTS[@]} -gt 0 ]]; then
    TEST_CMD=(dotnet test "${ARCH_PROJECTS[0]}" --no-build --nologo)
else
    TEST_CMD=(dotnet test "${SOLUTION_FILE}" --no-build --nologo --filter "${ARCH_TEST_FILTER}")
fi

if ! "${TEST_CMD[@]}" >"${TEST_LOG}" 2>&1; then
    printf "\033[1;31m[ORACLE:FAIL]\033[0m Stage 3 failed (Exit Code 30): Architecture or invariant test failure.\n" >&2
    parse_test_failures "${TEST_LOG}"
    exit 30
fi
printf "\033[1;32m[ORACLE:PASS]\033[0m Stage 3 passed: All architectural invariants verified.\n"

printf "\n\033[1;32m[ORACLE:VERIFIED]\033[0m All 3 deterministic verification gates succeeded (Exit Code 0).\n"
exit 0

