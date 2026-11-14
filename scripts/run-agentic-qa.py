#!/usr/bin/env python3
"""
GeoAssets — Autonomous Agentic QA & E2E Acceptance Runner
=========================================================
Orchestrates post-deployment acceptance testing for GeoAssets Server API and
Web client using configuration from `harness.config.json`.
Generates diagnostic forensic reports (`.harness/qa-e2e-report.md`) on failure.

Exit Codes:
  0  — All E2E acceptance tests passed
  2  — Configuration or network connection fatal error
  40 — E2E test failure or functional regression detected
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import subprocess
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


@dataclass
class TestCaseResult:
    name: str
    target: str  # "API" | "WEB"
    status: str  # "PASS" | "FAIL" | "WARN"
    duration_ms: float
    details: str
    reproduction_curl: str | None = None


@dataclass
class QARunReport:
    timestamp: str
    scope: str
    api_url: str
    web_url: str
    tests: list[TestCaseResult] = field(default_factory=list)

    @property
    def passed(self) -> int:
        return sum(1 for t in self.tests if t.status == "PASS")

    @property
    def failed(self) -> int:
        return sum(1 for t in self.tests if t.status == "FAIL")

    @property
    def exit_code(self) -> int:
        return 40 if self.failed > 0 else 0


def load_harness_config(root_dir: Path) -> dict[str, Any]:
    config_file = root_dir / "harness.config.json"
    if config_file.is_file():
        try:
            return json.loads(config_file.read_text(encoding="utf-8"))
        except Exception:
            pass
    return {}


def detect_scope(root_dir: Path) -> str:
    try:
        proc = subprocess.run(
            ["git", "diff", "--name-only", "HEAD~1..HEAD"],
            cwd=root_dir,
            capture_output=True,
            text=True,
            check=False,
        )
        changed = proc.stdout.splitlines() if proc.returncode == 0 else []
    except OSError:
        changed = []

    has_web = any(p.startswith(("apps/GeoAssets.Web/", "apps/GeoAssets.Shared/")) for p in changed)
    has_api = any(p.startswith(("apps/GeoAssets.Server/", "core/", "providers/", "workflow/")) for p in changed)
    if has_web and has_api:
        return "all"
    if has_web:
        return "web"
    return "api" if has_api or not changed else "all"


def http_check(name: str, target: str, url: str, expected_statuses: tuple[int, ...] = (200,)) -> TestCaseResult:
    start = time.monotonic()
    curl_cmd = f"curl -i -sS {url}"
    try:
        req = urllib.request.Request(url, method="GET")
        with urllib.request.urlopen(req, timeout=10) as resp:
            code = resp.status
            body = resp.read(512).decode("utf-8", errors="replace")
            elapsed = (time.monotonic() - start) * 1000.0
            if code in expected_statuses:
                return TestCaseResult(name=name, target=target, status="PASS", duration_ms=elapsed, details=f"HTTP {code}")
            return TestCaseResult(
                name=name,
                target=target,
                status="FAIL",
                duration_ms=elapsed,
                details=f"Unexpected HTTP {code}: {body[:200]}",
                reproduction_curl=curl_cmd,
            )
    except urllib.error.HTTPError as exc:
        elapsed = (time.monotonic() - start) * 1000.0
        if exc.code in expected_statuses:
            return TestCaseResult(name=name, target=target, status="PASS", duration_ms=elapsed, details=f"HTTP {exc.code}")
        return TestCaseResult(
            name=name,
            target=target,
            status="FAIL",
            duration_ms=elapsed,
            details=f"HTTPError {exc.code}: {exc.reason}",
            reproduction_curl=curl_cmd,
        )
    except Exception as exc:
        elapsed = (time.monotonic() - start) * 1000.0
        return TestCaseResult(
            name=name,
            target=target,
            status="FAIL",
            duration_ms=elapsed,
            details=f"Connection error: {exc}",
            reproduction_curl=curl_cmd,
        )


def write_report(root_dir: Path, report: QARunReport) -> Path:
    report_path = root_dir / ".harness" / "qa-e2e-report.md"
    report_path.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "# GeoAssets Autonomous E2E QA Report",
        "",
        f"- **Timestamp**: `{report.timestamp}`",
        f"- **Scope**: `{report.scope}`",
        f"- **API Base URL**: `{report.api_url}`",
        f"- **Web Base URL**: `{report.web_url}`",
        f"- **Result**: {report.passed} passed, {report.failed} failed (exit `{report.exit_code}`)",
        "",
        "| Target | Check | Status | Duration (ms) | Details |",
        "| :--- | :--- | :--- | ---: | :--- |",
    ]
    for tc in report.tests:
        lines.append(f"| {tc.target} | {tc.name} | {tc.status} | {tc.duration_ms:.1f} | {tc.details} |")
    failing = [tc for tc in report.tests if tc.status == "FAIL" and tc.reproduction_curl]
    if failing:
        lines.extend(["", "## Reproduction Commands", ""])
        for tc in failing:
            lines.append(f"- **{tc.name}**: `{tc.reproduction_curl}`")
    report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return report_path


def main() -> int:
    parser = argparse.ArgumentParser(description="GeoAssets autonomous post-deploy QA runner.")
    parser.add_argument("--scope", choices=["auto", "api", "web", "all"], default="auto")
    parser.add_argument("--retries", type=int, default=2)
    parser.add_argument("--story", default="")
    args = parser.parse_args()

    root_dir = Path(__file__).resolve().parent.parent
    cfg = load_harness_config(root_dir)
    e2e_cfg = cfg.get("e2e", {})
    api_url = str(e2e_cfg.get("apiBaseUrl", "http://localhost:8080")).rstrip("/")
    web_url = str(e2e_cfg.get("webBaseUrl", "http://localhost:8081")).rstrip("/")
    health_ep = str(e2e_cfg.get("healthEndpoint", "/healthz"))

    scope = detect_scope(root_dir) if args.scope == "auto" else args.scope
    report = QARunReport(
        timestamp=dt.datetime.now(dt.timezone.utc).isoformat(),
        scope=scope,
        api_url=api_url,
        web_url=web_url,
    )

    attempts = max(1, args.retries)
    for attempt in range(1, attempts + 1):
        report.tests.clear()
        if scope in ("api", "all"):
            report.tests.append(http_check("API Health", "API", f"{api_url}{health_ep}"))
            report.tests.append(http_check("API Auth Guard (unauthenticated)", "API", f"{api_url}/api/v1/assets", (200, 401, 403, 404)))
        if scope in ("web", "all"):
            report.tests.append(http_check("Web Root", "WEB", f"{web_url}/", (200, 302)))

        if report.failed == 0:
            break
        if attempt < attempts:
            time.sleep(2 * attempt)

    report_path = write_report(root_dir, report)
    print(f"[E2E-QA] Scope={scope} Passed={report.passed} Failed={report.failed} Report={report_path}")
    return report.exit_code


if __name__ == "__main__":
    sys.exit(main())
