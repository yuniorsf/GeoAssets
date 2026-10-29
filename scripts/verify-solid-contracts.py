#!/usr/bin/env python3
"""
GeoAssets — Deterministic SOLID, Categorical Scope & Structural Contract Verifier
=================================================================================
Complements `scripts/verify-agent-worktree.sh` with fast (<200ms) pre-flight and
post-synthesis AST/structural verification grounded in GeoAssets' C# 14 / .NET 10
Tri-Planar Dual-Loop architecture.

Exit Codes (aligned with `scripts/verify-agent-worktree.sh`):
  0  — All SOLID, scope, and structural checks passed
  2  — Invalid CLI arguments or target directory missing
  10 — DAG Scope / Disjoint File Ownership (2A ∩ 2B ≠ ∅) / .razor.cs split violation
  20 — Static guardrail violation (Banned clock/blocking APIs or analyzer suppression)
  30 — SOLID / Onion structural violation (core/ infra leak, DIP concrete adapter, LSP stub)
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import re
import subprocess
import sys
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Iterable

RAZOR_SINGLE_FILE_EXCEPTIONS = {
    "apps/GeoAssets.MAUI/Pages/MauiLogin.razor",
}

RAZOR_COMPONENT_PREFIXES = (
    "apps/GeoAssets.Shared/Components/",
    "apps/GeoAssets.Shared/Pages/",
    "apps/GeoAssets.Web/Components/",
    "apps/GeoAssets.Web/Pages/",
    "apps/GeoAssets.MAUI/Components/",
    "apps/GeoAssets.MAUI/Pages/",
)

FORBIDDEN_CORE_IMPORTS = (
    "Microsoft.EntityFrameworkCore",
    "Npgsql",
    "GeoAssets.Provider.",
    "GeoAssets.Server",
    "GeoAssets.Web",
    "GeoAssets.MAUI",
    "GeoAssets.Shared",
)


@dataclass(frozen=True)
class Finding:
    code: int
    severity: str  # "ERROR" | "ADVISORY"
    principle: str  # "SCOPE" | "RAZOR" | "GUARDRAIL" | "SRP" | "ISP" | "LSP" | "DIP"
    file: str
    line: int
    message: str


@dataclass
class AuditReport:
    target: str
    checked_files: list[str] = field(default_factory=list)
    findings: list[Finding] = field(default_factory=list)

    @property
    def errors(self) -> list[Finding]:
        return [f for f in self.findings if f.severity == "ERROR"]

    @property
    def advisories(self) -> list[Finding]:
        return [f for f in self.findings if f.severity == "ADVISORY"]

    @property
    def exit_code(self) -> int:
        err_codes = {f.code for f in self.errors}
        for candidate in (10, 20, 30):
            if candidate in err_codes:
                return candidate
        return 0


def strip_csharp_comments_and_strings(source: str) -> str:
    """
    Replaces C# comments and string literals with spaces while preserving newlines
    so line numbers remain 100% accurate and comments/strings never cause false positives.
    """
    pattern = re.compile(
        r"//[^\n]*"
        r"|/\*[\s\S]*?\*/"
        r'|@"(?:""|[^"])*"'
        r'|"(?:\\.|[^"\\])*"'
        r"|'(?:\\.|[^'\\])*'",
        re.MULTILINE,
    )

    def _blank(match: re.Match[str]) -> str:
        text = match.group(0)
        if text.startswith(('"', '@"', "'")):
            return '""' + "".join("\n" for ch in text if ch == "\n")
        return "".join("\n" if ch == "\n" else " " for ch in text)

    return pattern.sub(_blank, source)


def discover_changed_files(target_dir: Path, base_ref: str | None) -> list[str]:
    files: set[str] = set()
    try:
        status_proc = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=target_dir,
            capture_output=True,
            text=True,
            check=False,
        )
        if status_proc.returncode == 0:
            for raw_line in status_proc.stdout.splitlines():
                if len(raw_line) < 4:
                    continue
                status = raw_line[:2]
                if "D" in status:
                    continue
                rel = raw_line[3:].strip()
                if " -> " in rel:
                    rel = rel.split(" -> ", 1)[1].strip()
                files.add(rel.strip('"'))

        if base_ref:
            diff_proc = subprocess.run(
                ["git", "diff", "--name-only", "--diff-filter=ACMR", f"{base_ref}...HEAD"],
                cwd=target_dir,
                capture_output=True,
                text=True,
                check=False,
            )
            if diff_proc.returncode == 0:
                for line in diff_proc.stdout.splitlines():
                    if line.strip():
                        files.add(line.strip())
    except OSError:
        pass
    return sorted(files)


def matches_any_pattern(rel_path: str, patterns: Iterable[str]) -> bool:
    norm = rel_path.replace("\\", "/")
    for pat in patterns:
        p = pat.strip().replace("\\", "/")
        if not p:
            continue
        if any(ch in p for ch in ("*", "?", "[")):
            if fnmatch.fnmatch(norm, p):
                return True
        elif norm == p or norm.startswith(p.rstrip("/") + "/"):
            return True
    return False


def verify_scopes(
    changed_files: list[str],
    scope_a: list[str],
    scope_b: list[str],
    allowed_scope: list[str],
) -> list[Finding]:
    findings: list[Finding] = []

    if scope_a and scope_b:
        # Check literal pattern overlap plus concrete file overlap (2A ∩ 2B = ∅)
        exact_overlap = set(scope_a) & set(scope_b)
        for item in sorted(exact_overlap):
            findings.append(
                Finding(
                    code=10,
                    severity="ERROR",
                    principle="SCOPE",
                    file=item,
                    line=1,
                    message=f"Monoidal disjointness violated (2A ∩ 2B ≠ ∅): '{item}' appears in both Branch 2A and Branch 2B scopes.",
                )
            )
        for rel in changed_files:
            in_a = matches_any_pattern(rel, scope_a)
            in_b = matches_any_pattern(rel, scope_b)
            if in_a and in_b:
                findings.append(
                    Finding(
                        code=10,
                        severity="ERROR",
                        principle="SCOPE",
                        file=rel,
                        line=1,
                        message="File matches both Branch 2A and Branch 2B ownership patterns (2A ∩ 2B ≠ ∅).",
                    )
                )
            elif not (in_a or in_b):
                findings.append(
                    Finding(
                        code=10,
                        severity="ERROR",
                        principle="SCOPE",
                        file=rel,
                        line=1,
                        message="SRP mutation scope violated: modified file is outside both Branch 2A and Branch 2B assigned scopes.",
                    )
                )

    if allowed_scope:
        for rel in changed_files:
            if not matches_any_pattern(rel, allowed_scope):
                findings.append(
                    Finding(
                        code=10,
                        severity="ERROR",
                        principle="SCOPE",
                        file=rel,
                        line=1,
                        message="SRP mutation scope violated: modified file lies outside agent's allowed bounded scope.",
                    )
                )

    return findings


def verify_razor_split(target_dir: Path, rel_path: str) -> list[Finding]:
    norm = rel_path.replace("\\", "/")
    if not norm.endswith(".razor"):
        return []
    if norm in RAZOR_SINGLE_FILE_EXCEPTIONS or norm.endswith("_Imports.razor"):
        return []
    if not norm.startswith(RAZOR_COMPONENT_PREFIXES):
        return []

    code_behind = target_dir / f"{norm}.cs"
    razor_file = target_dir / norm
    findings: list[Finding] = []
    if not code_behind.exists():
        findings.append(
            Finding(
                code=10,
                severity="ERROR",
                principle="RAZOR",
                file=norm,
                line=1,
                message=f"Missing required code-behind partial class '{norm}.cs' (GeoAssets .razor + .razor.cs convention).",
            )
        )
    if razor_file.exists():
        text = razor_file.read_text(encoding="utf-8", errors="replace")
        for idx, line in enumerate(text.splitlines(), start=1):
            if re.search(r"^\s*@code\s*\{", line):
                findings.append(
                    Finding(
                        code=10,
                        severity="ERROR",
                        principle="RAZOR",
                        file=norm,
                        line=idx,
                        message="Inline '@code' block found in .razor markup; move C# logic into the '.razor.cs' partial class.",
                    )
                )
    return findings


def verify_msbuild_suppressions(target_dir: Path, rel_path: str) -> list[Finding]:
    norm = rel_path.replace("\\", "/")
    if not (norm.endswith(".csproj") or norm.endswith(".props") or norm.endswith(".targets")):
        return []
    full_path = target_dir / norm
    if not full_path.exists():
        return []
    findings: list[Finding] = []
    for idx, line in enumerate(full_path.read_text(encoding="utf-8", errors="replace").splitlines(), start=1):
        if "<NoWarn>" in line:
            findings.append(
                Finding(
                    code=20,
                    severity="ERROR",
                    principle="GUARDRAIL",
                    file=norm,
                    line=idx,
                    message="Analyzer suppression '<NoWarn>' is prohibited by GeoAssets guardrails; resolve the underlying diagnostic.",
                )
            )
    return findings


def extract_brace_block(lines: list[str], start_idx: int) -> tuple[int, list[tuple[int, str]]]:
    """Extracts lines belonging to the first `{ ... }` block starting at or after `start_idx`."""
    depth = 0
    started = False
    collected: list[tuple[int, str]] = []
    for idx in range(start_idx, len(lines)):
        line = lines[idx]
        for ch in line:
            if ch == "{":
                depth += 1
                started = True
            elif ch == "}":
                depth -= 1
        if started:
            collected.append((idx + 1, line))
            if depth <= 0:
                return idx, collected
        elif ";" in line:
            # Primary constructor / empty body declaration ending with `;`
            return idx, collected
    return len(lines) - 1, collected


def audit_csharp_file(
    target_dir: Path,
    rel_path: str,
    max_interface_methods: int,
    max_public_methods: int,
    max_loc: int,
    strict_metrics: bool,
) -> list[Finding]:
    norm = rel_path.replace("\\", "/")
    if not norm.endswith(".cs"):
        return []
    full_path = target_dir / norm
    if not full_path.exists():
        return []

    raw_source = full_path.read_text(encoding="utf-8", errors="replace")
    clean_source = strip_csharp_comments_and_strings(raw_source)
    raw_lines = raw_source.splitlines()
    clean_lines = clean_source.splitlines()

    is_core = norm.startswith("core/")
    is_test = norm.startswith("tests/") or ".Tests/" in norm or ".TestKit/" in norm

    findings: list[Finding] = []
    advisory_sev = "ERROR" if strict_metrics else "ADVISORY"

    # 1. Line-level Guardrails, Onion Purity, LSP & DIP Checks
    for idx, line in enumerate(clean_lines, start=1):
        raw_line = raw_lines[idx - 1] if idx - 1 < len(raw_lines) else line

        # Analyzer suppressions (check raw_line for #pragma since preprocessor isn't a comment, clean_line for attributes)
        if re.search(r"^\s*#\s*pragma\s+warning\s+disable\b", raw_line):
            findings.append(
                Finding(
                    code=20,
                    severity="ERROR",
                    principle="GUARDRAIL",
                    file=norm,
                    line=idx,
                    message="Prohibited '#pragma warning disable' directive; fix the underlying compiler/analyzer defect.",
                )
            )
        if re.search(r"\[\s*(?:System\.Diagnostics\.CodeAnalysis\.)?SuppressMessage\b", line):
            findings.append(
                Finding(
                    code=20,
                    severity="ERROR",
                    principle="GUARDRAIL",
                    file=norm,
                    line=idx,
                    message="Prohibited '[SuppressMessage]' attribute; fix the underlying analyzer defect.",
                )
            )

        # Banned system clock calls (RS0030)
        clock_match = re.search(r"\b(DateTime(?:Offset)?\.(?:UtcNow|Now|Today))\b", line)
        if clock_match:
            findings.append(
                Finding(
                    code=20,
                    severity="ERROR",
                    principle="GUARDRAIL",
                    file=norm,
                    line=idx,
                    message=f"Banned clock symbol '{clock_match.group(1)}' (RS0030); inject System.TimeProvider (or FakeTimeProvider in tests).",
                )
            )

        # Banned blocking / sync-over-async calls
        blocking_match = re.search(
            r"(\.GetAwaiter\s*\(\s*\)\s*\.GetResult\s*\(|\bTask\.Wait(?:All|Any)?\s*\(|\bThread\.Sleep\s*\()",
            line,
        )
        if blocking_match:
            findings.append(
                Finding(
                    code=20,
                    severity="ERROR",
                    principle="GUARDRAIL",
                    file=norm,
                    line=idx,
                    message=f"Banned blocking call '{blocking_match.group(1).strip()}'; use non-blocking 'await' with CancellationToken.",
                )
            )

        # SRP / Onion Purity in core/
        if is_core:
            for forbidden in FORBIDDEN_CORE_IMPORTS:
                if re.search(rf"\busing\s+(?:static\s+)?{re.escape(forbidden)}", line):
                    findings.append(
                        Finding(
                            code=30,
                            severity="ERROR",
                            principle="SRP",
                            file=norm,
                            line=idx,
                            message=f"Onion Architecture / SRP violation: 'core/' file references forbidden outer-layer namespace '{forbidden}'.",
                        )
                    )
            dip_match = re.search(
                r"\bnew\s+(HttpClient|[A-Z]\w*DbContext|NpgsqlConnection|SqlConnection|Postgres[A-Z]\w*|Rest[A-Z]\w*Repository)\s*\(",
                line,
            )
            if dip_match:
                findings.append(
                    Finding(
                        code=30,
                        severity="ERROR",
                        principle="DIP",
                        file=norm,
                        line=idx,
                        message=f"DIP violation in 'core/': direct instantiation of concrete adapter '{dip_match.group(1)}'; inject an interface port.",
                    )
                )

        # LSP Stub Traps in production code
        if not is_test:
            if re.search(r"\bthrow\s+new\s+NotImplementedException\s*\(", line):
                findings.append(
                    Finding(
                        code=30,
                        severity="ERROR",
                        principle="LSP",
                        file=norm,
                        line=idx,
                        message="LSP / ISP violation: production code throws 'NotImplementedException'. Implement contract or segregate interface.",
                    )
                )
            if re.search(r"\bthrow\s+new\s+NotSupportedException\s*\(", line):
                findings.append(
                    Finding(
                        code=30,
                        severity=advisory_sev,
                        principle="LSP",
                        file=norm,
                        line=idx,
                        message="Potential LSP violation: 'NotSupportedException' in production type; verify read/write interfaces are properly segregated (ISP).",
                    )
                )

    # 2. Structural Metrics (ISP interface granularity & SRP class cohesion) on production files
    if not is_test:
        if len(clean_lines) > max_loc:
            findings.append(
                Finding(
                    code=30,
                    severity=advisory_sev,
                    principle="SRP",
                    file=norm,
                    line=1,
                    message=f"File has {len(clean_lines)} LOC (> {max_loc}); check whether multiple responsibilities/axes of change were merged.",
                )
            )

        for idx, line in enumerate(clean_lines):
            iface_match = re.search(r"\binterface\s+(I[A-Za-z0-9_]+)\b", line)
            if iface_match:
                iface_name = iface_match.group(1)
                _, block = extract_brace_block(clean_lines, idx)
                # Count top-level method signatures inside interface body (depth == 1)
                depth = 0
                method_count = 0
                for _, bline in block:
                    if depth == 1 and re.search(r"\b[A-Za-z0-9_<>,?.\[\]]+\s+[A-Za-z0-9_]+\s*(?:<[^>]+>)?\s*\(", bline):
                        method_count += 1
                    depth += bline.count("{") - bline.count("}")
                if method_count > max_interface_methods:
                    findings.append(
                        Finding(
                            code=30,
                            severity=advisory_sev,
                            principle="ISP",
                            file=norm,
                            line=idx + 1,
                            message=(
                                f"Interface '{iface_name}' declares {method_count} methods (> {max_interface_methods}); "
                                "consider role-based segregation (e.g., I*Reader vs. I*Writer)."
                            ),
                        )
                    )

            class_match = re.search(r"\bclass\s+([A-Za-z0-9_]+)\b", line)
            if class_match and "partial " not in line:
                class_name = class_match.group(1)
                _, block = extract_brace_block(clean_lines, idx)
                depth = 0
                pub_methods = 0
                for _, bline in block:
                    if (
                        depth == 1
                        and re.search(r"\bpublic\b", bline)
                        and "(" in bline
                        and not re.search(rf"\bpublic\s+{re.escape(class_name)}\s*\(", bline)
                    ):
                        pub_methods += 1
                    depth += bline.count("{") - bline.count("}")
                if pub_methods > max_public_methods:
                    findings.append(
                        Finding(
                            code=30,
                            severity=advisory_sev,
                            principle="SRP",
                            file=norm,
                            line=idx + 1,
                            message=(
                                f"Class '{class_name}' exposes {pub_methods} public methods (> {max_public_methods}); "
                                "verify cohesion (LCOM) and single domain mutation axis."
                            ),
                        )
                    )

    return findings


def parse_csv_patterns(raw_values: list[str] | None) -> list[str]:
    if not raw_values:
        return []
    result: list[str] = []
    for item in raw_values:
        for part in item.split(","):
            if part.strip():
                result.append(part.strip())
    return result


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Verify SOLID, Onion Architecture, and Categorical DAG scope invariants on GeoAssets files."
    )
    parser.add_argument("--target", default=".", help="Target repository or worktree root directory (default: current directory).")
    parser.add_argument("--files", nargs="*", help="Explicit relative file paths to audit (overrides git diff discovery).")
    parser.add_argument("--base", default=None, help="Optional git base ref (e.g., origin/develop) to include committed branch diffs.")
    parser.add_argument("--scope-a", action="append", help="Comma-separated path prefixes/globs owned by Branch 2A (Claude Code).")
    parser.add_argument("--scope-b", action="append", help="Comma-separated path prefixes/globs owned by Branch 2B (GitHub Copilot).")
    parser.add_argument("--allowed-scope", action="append", help="Comma-separated path prefixes/globs permitted for a single agent node.")
    parser.add_argument("--max-interface-methods", type=int, default=5, help="ISP advisory threshold for methods per interface (default: 5).")
    parser.add_argument("--max-public-methods", type=int, default=10, help="SRP advisory threshold for public methods per class (default: 10).")
    parser.add_argument("--max-loc", type=int, default=500, help="SRP advisory threshold for lines of code per C# file (default: 500).")
    parser.add_argument("--strict-metrics", action="store_true", help="Promote ISP/SRP metric advisories to Exit Code 30 errors.")
    parser.add_argument("--json", action="store_true", help="Emit structured JSON report for Outer-Loop Error Monad consumption.")
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    target_dir = Path(args.target).resolve()
    if not target_dir.is_dir():
        print(f"[SOLID-ORACLE:FATAL] Target directory does not exist: {target_dir}", file=sys.stderr)
        return 2

    if args.files is not None and len(args.files) > 0:
        checked_files = sorted({str(Path(f)).replace("\\", "/") for f in args.files})
    else:
        checked_files = discover_changed_files(target_dir, args.base)

    scope_a = parse_csv_patterns(args.scope_a)
    scope_b = parse_csv_patterns(args.scope_b)
    allowed_scope = parse_csv_patterns(args.allowed_scope)

    report = AuditReport(target=str(target_dir), checked_files=checked_files)
    report.findings.extend(verify_scopes(checked_files, scope_a, scope_b, allowed_scope))

    for rel in checked_files:
        report.findings.extend(verify_razor_split(target_dir, rel))
        report.findings.extend(verify_msbuild_suppressions(target_dir, rel))
        report.findings.extend(
            audit_csharp_file(
                target_dir=target_dir,
                rel_path=rel,
                max_interface_methods=args.max_interface_methods,
                max_public_methods=args.max_public_methods,
                max_loc=args.max_loc,
                strict_metrics=args.strict_metrics,
            )
        )

    if args.json:
        payload = {
            "target": report.target,
            "checked_files": report.checked_files,
            "passed": report.exit_code == 0,
            "exit_code": report.exit_code,
            "error_count": len(report.errors),
            "advisory_count": len(report.advisories),
            "findings": [asdict(f) for f in report.findings],
        }
        print(json.dumps(payload, indent=2))
        return report.exit_code

    print(f"[SOLID-ORACLE] Target: {report.target} | Audited files: {len(report.checked_files)}")
    for finding in report.findings:
        tag = "FAIL" if finding.severity == "ERROR" else "WARN"
        stream = sys.stderr if finding.severity == "ERROR" else sys.stdout
        print(
            f"[SOLID-ORACLE:{tag}] (Code {finding.code} | {finding.principle}) "
            f"{finding.file}:{finding.line} — {finding.message}",
            file=stream,
        )

    if report.exit_code == 0:
        print(
            f"[SOLID-ORACLE:VERIFIED] Passed (0 errors, {len(report.advisories)} advisories)."
        )
    else:
        print(
            f"[SOLID-ORACLE:HALT] Failed with Exit Code {report.exit_code} "
            f"({len(report.errors)} errors, {len(report.advisories)} advisories).",
            file=sys.stderr,
        )
    return report.exit_code


if __name__ == "__main__":
    sys.exit(main())

