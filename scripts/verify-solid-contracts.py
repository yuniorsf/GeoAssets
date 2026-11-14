#!/usr/bin/env python3
"""
Polyglot SOLID, Categorical Disjoint-Scope & Guardrail Contract Verifier
========================================================================
Language-agnostic static & structural verifier configured by `harness.config.json`.
Supports Python, TypeScript/JavaScript, Go, Rust, C#, Java, and Kotlin projects.

Exit Codes (aligned with `scripts/verify-agent-worktree.sh`):
  0  — All SOLID, scope, and guardrail checks passed
  2  — Invalid CLI arguments or target directory missing
  10 — DAG Scope / Disjoint File Ownership (2A ∩ 2B ≠ ∅) / Paired-file rule violation
  20 — Static guardrail violation (Linter/typechecker suppression or banned API usage)
  30 — SOLID / Clean-Hexagonal structural violation (Domain core infra leak, DIP adapter, LSP stub)
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
from typing import Any, Iterable

DEFAULT_CONFIG: dict[str, Any] = {
    "architecture": {
        "domainCoreDirs": ["src/core", "src/domain", "core", "domain"],
        "testDirs": ["tests", "test", "__tests__", "spec"],
        "sourceExtensions": [".py", ".ts", ".tsx", ".js", ".jsx", ".go", ".rs", ".cs", ".java", ".kt"],
        "forbiddenCoreImports": [],
        "concreteAdapterInstantiationsInCore": [],
        "bannedApiPatterns": [],
        "suppressionPatterns": [],
        "unimplementedStubPatterns": [],
        "pairedFileRules": [],
        "metrics": {
            "maxInterfaceMethods": 5,
            "maxPublicMethods": 10,
            "maxLoc": 500,
        },
    }
}


@dataclass(frozen=True)
class Finding:
    code: int
    severity: str  # "ERROR" | "ADVISORY"
    principle: str  # "SCOPE" | "PAIRING" | "GUARDRAIL" | "SRP" | "ISP" | "LSP" | "DIP"
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


def load_config(target_dir: Path, config_override: str | None) -> dict[str, Any]:
    cfg_path = Path(config_override) if config_override else (target_dir / "harness.config.json")
    if not cfg_path.exists():
        script_root_cfg = Path(__file__).resolve().parent.parent / "harness.config.json"
        if script_root_cfg.exists():
            cfg_path = script_root_cfg
    if cfg_path.exists():
        return json.loads(cfg_path.read_text(encoding="utf-8"))
    return DEFAULT_CONFIG


def strip_comments_preserve_lines(source: str, ext: str) -> str:
    """Strips comments (keeping string literals blanked) while preserving exact line numbers."""
    if ext == ".py":
        pattern = re.compile(
            r'"""[\s\S]*?"""|\'\'\'[\s\S]*?\'\'\'|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|#[^\n]*',
            re.MULTILINE,
        )
    else:
        pattern = re.compile(
            r"//[^\n]*|/\*[\s\S]*?\*/|`(?:\\.|[^`\\])*`|\"(?:\\.|[^\"\\])*\"|'(?:\\.|[^'\\])*'",
            re.MULTILINE,
        )

    def _blank(match: re.Match[str]) -> str:
        text = match.group(0)
        if text.startswith(('"', "'", "`")):
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


def is_test_file(norm_path: str, test_dirs: list[str]) -> bool:
    if matches_any_pattern(norm_path, test_dirs):
        return True
    name = Path(norm_path).name
    return (
        name.startswith("test_")
        or name.endswith(("_test.py", "_test.go", "_test.rs", ".test.ts", ".test.tsx", ".spec.ts", ".spec.tsx", "Tests.cs"))
        or ".Tests/" in norm_path
    )


def verify_scopes(
    changed_files: list[str],
    scope_a: list[str],
    scope_b: list[str],
    allowed_scope: list[str],
) -> list[Finding]:
    findings: list[Finding] = []
    if scope_a and scope_b:
        for item in sorted(set(scope_a) & set(scope_b)):
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


def verify_paired_files(target_dir: Path, rel_path: str, rules: list[dict[str, Any]]) -> list[Finding]:
    norm = rel_path.replace("\\", "/")
    findings: list[Finding] = []
    for rule in rules:
        ext = rule.get("extension", "")
        companion_suffix = rule.get("companionSuffix", "")
        prefixes = rule.get("pathPrefixes", [])
        exceptions = set(rule.get("exceptions", []))
        if not ext or not companion_suffix or not norm.endswith(ext) or norm in exceptions:
            continue
        if prefixes and not any(norm.startswith(p) for p in prefixes):
            continue
        companion = target_dir / f"{norm}{companion_suffix}"
        if not companion.exists():
            findings.append(
                Finding(
                    code=10,
                    severity="ERROR",
                    principle="PAIRING",
                    file=norm,
                    line=1,
                    message=f"Missing required companion file '{norm}{companion_suffix}'.",
                )
            )
    return findings


def rule_applies_to_ext(rule: dict[str, Any], ext: str) -> bool:
    exts = rule.get("extensions")
    return not exts or ext in exts


def audit_file(
    target_dir: Path,
    rel_path: str,
    arch_cfg: dict[str, Any],
    max_interface_methods: int,
    max_public_methods: int,
    max_loc: int,
    strict_metrics: bool,
) -> list[Finding]:
    norm = rel_path.replace("\\", "/")
    full_path = target_dir / norm
    if not full_path.is_file():
        return []

    ext = full_path.suffix
    raw_source = full_path.read_text(encoding="utf-8", errors="replace")
    raw_lines = raw_source.splitlines()

    findings: list[Finding] = []

    # 1. Suppression checks run against raw lines (since linter directives often sit inside comments!)
    for rule in arch_cfg.get("suppressionPatterns", []):
        if not rule_applies_to_ext(rule, ext):
            continue
        regex = re.compile(rule["pattern"])
        for idx, rline in enumerate(raw_lines, start=1):
            if regex.search(rline):
                findings.append(
                    Finding(
                        code=20,
                        severity="ERROR",
                        principle="GUARDRAIL",
                        file=norm,
                        line=idx,
                        message=rule.get("message", "Linter/compiler suppression directive is prohibited."),
                    )
                )

    source_exts = set(arch_cfg.get("sourceExtensions", []))
    if ext not in source_exts:
        return findings

    clean_source = strip_comments_preserve_lines(raw_source, ext)
    clean_lines = clean_source.splitlines()
    is_core = matches_any_pattern(norm, arch_cfg.get("domainCoreDirs", []))
    is_test = is_test_file(norm, arch_cfg.get("testDirs", []))
    advisory_sev = "ERROR" if strict_metrics else "ADVISORY"

    # 2. Banned APIs, Unimplemented Stubs (LSP), and Domain Core Purity (SRP/DIP)
    for idx, rline in enumerate(raw_lines, start=1):
        cline = clean_lines[idx - 1] if idx - 1 < len(clean_lines) else rline

        for rule in arch_cfg.get("bannedApiPatterns", []):
            if rule_applies_to_ext(rule, ext) and re.search(rule["pattern"], cline):
                findings.append(
                    Finding(
                        code=20,
                        severity="ERROR",
                        principle="GUARDRAIL",
                        file=norm,
                        line=idx,
                        message=rule.get("message", "Banned API detected."),
                    )
                )

        if not is_test:
            # Check raw line (excluding pure comments) for stubs whose message might be in string literals
            code_part = rline.split("//", 1)[0].split("#", 1)[0] if ext != ".cs" else rline.split("//", 1)[0]
            for stub_rule in arch_cfg.get("unimplementedStubPatterns", []):
                if rule_applies_to_ext(stub_rule, ext) and re.search(stub_rule["pattern"], code_part):
                    findings.append(
                        Finding(
                            code=30,
                            severity="ERROR",
                            principle="LSP",
                            file=norm,
                            line=idx,
                            message="LSP / ISP violation: production code contains an unimplemented stub; implement contract or segregate interface.",
                        )
                    )

        if is_core and not is_test:
            for forbidden in arch_cfg.get("forbiddenCoreImports", []):
                # Match import/using/use/require statements referencing forbidden outer-layer modules
                if re.search(
                    rf"(?:\bimport\b.*?\b{re.escape(forbidden)}\b|\bfrom\s+{re.escape(forbidden)}\b|\busing\s+{re.escape(forbidden)}|\buse\s+{re.escape(forbidden)}|\"{re.escape(forbidden)})",
                    rline,
                ):
                    findings.append(
                        Finding(
                            code=30,
                            severity="ERROR",
                            principle="SRP",
                            file=norm,
                            line=idx,
                            message=f"Clean/Hexagonal Architecture violation: domain core file imports outer-layer package '{forbidden}'.",
                        )
                    )
            for dip_pat in arch_cfg.get("concreteAdapterInstantiationsInCore", []):
                if re.search(dip_pat, cline):
                    findings.append(
                        Finding(
                            code=30,
                            severity="ERROR",
                            principle="DIP",
                            file=norm,
                            line=idx,
                            message="DIP violation in domain core: direct instantiation of concrete infrastructure adapter; inject an abstract port.",
                        )
                    )

    # 3. Polyglot ISP & SRP Structural Metrics on production files
    if not is_test:
        if len(clean_lines) > max_loc:
            findings.append(
                Finding(
                    code=30,
                    severity=advisory_sev,
                    principle="SRP",
                    file=norm,
                    line=1,
                    message=f"File has {len(clean_lines)} LOC (> {max_loc}); verify single responsibility and module cohesion.",
                )
            )
        findings.extend(scan_interface_granularity(norm, ext, clean_lines, max_interface_methods, advisory_sev))
        findings.extend(scan_class_cohesion(norm, ext, clean_lines, max_public_methods, advisory_sev))

    return findings


def scan_interface_granularity(
    norm: str,
    ext: str,
    clean_lines: list[str],
    max_methods: int,
    severity: str,
) -> list[Finding]:
    findings: list[Finding] = []
    if ext == ".py":
        for idx, line in enumerate(clean_lines):
            m = re.match(r"^(\s*)class\s+([A-Za-z0-9_]+)\s*\(([^)]*(?:Protocol|ABC)[^)]*)\)\s*:", line)
            if m:
                indent = len(m.group(1))
                name = m.group(2)
                methods = 0
                for j in range(idx + 1, len(clean_lines)):
                    sub = clean_lines[j]
                    if not sub.strip():
                        continue
                    cur_indent = len(sub) - len(sub.lstrip())
                    if cur_indent <= indent:
                        break
                    if re.match(r"^\s*def\s+(?!__)([A-Za-z0-9_]+)\s*\(", sub):
                        methods += 1
                if methods > max_methods:
                    findings.append(
                        Finding(
                            code=30,
                            severity=severity,
                            principle="ISP",
                            file=norm,
                            line=idx + 1,
                            message=f"Protocol/ABC '{name}' declares {methods} methods (> {max_methods}); segregate into role-specific ports.",
                        )
                    )
        return findings

    iface_regexes = {
        ".ts": r"\binterface\s+([A-Za-z0-9_]+)",
        ".tsx": r"\binterface\s+([A-Za-z0-9_]+)",
        ".cs": r"\binterface\s+(I[A-Za-z0-9_]+)",
        ".java": r"\binterface\s+([A-Za-z0-9_]+)",
        ".kt": r"\binterface\s+([A-Za-z0-9_]+)",
        ".go": r"\btype\s+([A-Za-z0-9_]+)\s+interface\b",
        ".rs": r"\btrait\s+([A-Za-z0-9_]+)",
    }
    rx = iface_regexes.get(ext)
    if not rx:
        return findings

    for idx, line in enumerate(clean_lines):
        m = re.search(rx, line)
        if not m:
            continue
        name = m.group(1)
        depth = 0
        started = False
        methods = 0
        for j in range(idx, len(clean_lines)):
            bline = clean_lines[j]
            if started and depth == 1 and "(" in bline:
                methods += 1
            for ch in bline:
                if ch == "{":
                    depth += 1
                    started = True
                elif ch == "}":
                    depth -= 1
            if started and depth <= 0:
                break
        if methods > max_methods:
            findings.append(
                Finding(
                    code=30,
                    severity=severity,
                    principle="ISP",
                    file=norm,
                    line=idx + 1,
                    message=f"Interface/Trait '{name}' declares {methods} methods (> {max_methods}); consider role-based segregation (Reader vs. Writer).",
                )
            )
    return findings


def scan_class_cohesion(
    norm: str,
    ext: str,
    clean_lines: list[str],
    max_public_methods: int,
    severity: str,
) -> list[Finding]:
    findings: list[Finding] = []
    if ext == ".py":
        for idx, line in enumerate(clean_lines):
            m = re.match(r"^(\s*)class\s+([A-Za-z0-9_]+)(?:\((?![^)]*(?:Protocol|ABC))[^)]*\))?\s*:", line)
            if m:
                indent = len(m.group(1))
                name = m.group(2)
                pub_methods = 0
                for j in range(idx + 1, len(clean_lines)):
                    sub = clean_lines[j]
                    if not sub.strip():
                        continue
                    cur_indent = len(sub) - len(sub.lstrip())
                    if cur_indent <= indent:
                        break
                    if re.match(r"^\s*def\s+(?!_)([A-Za-z0-9_]+)\s*\(", sub):
                        pub_methods += 1
                if pub_methods > max_public_methods:
                    findings.append(
                        Finding(
                            code=30,
                            severity=severity,
                            principle="SRP",
                            file=norm,
                            line=idx + 1,
                            message=f"Class '{name}' exposes {pub_methods} public methods (> {max_public_methods}); verify single axis of change.",
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
        description="Polyglot SOLID, Clean/Hexagonal Architecture, and Categorical DAG scope verifier."
    )
    parser.add_argument("--target", default=".", help="Target repository or worktree root directory.")
    parser.add_argument("--config", default=None, help="Optional path to harness.config.json.")
    parser.add_argument("--files", nargs="*", help="Explicit relative file paths to audit (overrides git diff discovery).")
    parser.add_argument("--base", default=None, help="Optional git base ref (e.g., origin/develop) to include branch diffs.")
    parser.add_argument("--scope-a", action="append", help="Comma-separated path prefixes/globs owned by Branch 2A (Claude Code).")
    parser.add_argument("--scope-b", action="append", help="Comma-separated path prefixes/globs owned by Branch 2B (GitHub Copilot).")
    parser.add_argument("--allowed-scope", action="append", help="Comma-separated path prefixes/globs permitted for a single agent node.")
    parser.add_argument("--max-interface-methods", type=int, default=None, help="Override ISP advisory threshold for methods per interface.")
    parser.add_argument("--max-public-methods", type=int, default=None, help="Override SRP advisory threshold for public methods per class.")
    parser.add_argument("--max-loc", type=int, default=None, help="Override SRP advisory threshold for lines of code per file.")
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

    cfg = load_config(target_dir, args.config)
    arch_cfg = cfg.get("architecture", DEFAULT_CONFIG["architecture"])
    metrics_cfg = arch_cfg.get("metrics", {})

    max_iface = args.max_interface_methods or int(metrics_cfg.get("maxInterfaceMethods", 5))
    max_pub = args.max_public_methods or int(metrics_cfg.get("maxPublicMethods", 10))
    max_loc = args.max_loc or int(metrics_cfg.get("maxLoc", 500))

    if args.files is not None and len(args.files) > 0:
        checked_files = sorted({str(Path(f)).replace("\\", "/") for f in args.files})
    else:
        checked_files = discover_changed_files(target_dir, args.base)

    scope_a = parse_csv_patterns(args.scope_a)
    scope_b = parse_csv_patterns(args.scope_b)
    allowed_scope = parse_csv_patterns(args.allowed_scope)

    report = AuditReport(target=str(target_dir), checked_files=checked_files)
    report.findings.extend(verify_scopes(checked_files, scope_a, scope_b, allowed_scope))

    paired_rules = arch_cfg.get("pairedFileRules", [])
    for rel in checked_files:
        report.findings.extend(verify_paired_files(target_dir, rel, paired_rules))
        report.findings.extend(
            audit_file(
                target_dir=target_dir,
                rel_path=rel,
                arch_cfg=arch_cfg,
                max_interface_methods=max_iface,
                max_public_methods=max_pub,
                max_loc=max_loc,
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
        print(f"[SOLID-ORACLE:VERIFIED] Passed (0 errors, {len(report.advisories)} advisories).")
    else:
        print(
            f"[SOLID-ORACLE:HALT] Failed with Exit Code {report.exit_code} "
            f"({len(report.errors)} errors, {len(report.advisories)} advisories).",
            file=sys.stderr,
        )
    return report.exit_code


if __name__ == "__main__":
    sys.exit(main())
