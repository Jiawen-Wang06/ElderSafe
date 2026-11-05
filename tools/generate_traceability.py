#!/usr/bin/env python3
"""Generate the requirements traceability matrix from test results.

Reads the requirements register from docs/04-software-requirements.md and the
test results produced by CI, then writes docs/traceability.md.

Test frameworks tag each test with the requirement it verifies:

    xUnit       [Trait("Requirement", "REQ-FALL-03")]
    pytest      @pytest.mark.req("REQ-FALL-01")
    Playwright  test("...", { tag: "@REQ-FALL-01" }, async () => {...})

Those tags reach this script through the result files:

    *.trx         .NET test results   (dotnet test --logger trx)
    *.xml         JUnit XML           (pytest --junitxml, Playwright junit reporter)

Exit codes:
    0   every requirement has at least one passing test
    1   at least one requirement has no passing test, or a tag names an
        unknown requirement

Usage:
    python tools/generate_traceability.py --results artifacts/ --check
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path

# Underscore counts as a word character, so \b would not match in
# "test_fall_REQ-FALL-01". Use explicit look-around instead.
REQ_PATTERN = re.compile(r"(?<![A-Za-z0-9])REQ-[A-Z]+-\d{2}(?![0-9])")
TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


@dataclass
class Requirement:
    req_id: str
    text: str
    sources: str
    tests: list[tuple[str, str]] = field(default_factory=list)

    @property
    def passing(self) -> list[str]:
        return [name for name, outcome in self.tests if outcome == "passed"]

    @property
    def failing(self) -> list[str]:
        return [name for name, outcome in self.tests if outcome == "failed"]

    @property
    def status(self) -> str:
        if self.failing:
            return "FAIL"
        if self.passing:
            return "PASS"
        return "NO TEST"


def parse_requirements(doc: Path) -> dict[str, Requirement]:
    """Pull requirements out of the markdown tables in the requirements doc."""
    requirements: dict[str, Requirement] = {}
    for line in doc.read_text(encoding="utf-8").splitlines():
        if not line.startswith("| REQ-"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) < 4:
            continue
        req_id = cells[0]
        if not REQ_PATTERN.fullmatch(req_id):
            continue
        requirements[req_id] = Requirement(
            req_id=req_id, text=cells[1], sources=cells[3]
        )
    return requirements


def parse_trx(path: Path) -> list[tuple[str, str, list[str]]]:
    """Return (test name, outcome, requirement ids) for each test in a .trx file."""
    root = ET.parse(path).getroot()

    traits: dict[str, list[str]] = {}
    for definition in root.iterfind(".//t:UnitTest", TRX_NS):
        name = definition.get("name") or ""
        ids: list[str] = []
        for trait in definition.iterfind(".//t:TestCategory/t:TestCategoryItem", TRX_NS):
            ids += REQ_PATTERN.findall(trait.get("TestCategory") or "")
        for prop in definition.iterfind(".//t:Properties/t:Property", TRX_NS):
            key = prop.findtext("t:Key", default="", namespaces=TRX_NS)
            if key == "Requirement":
                value = prop.findtext("t:Value", default="", namespaces=TRX_NS)
                ids += REQ_PATTERN.findall(value)
        # Fall back to the test's own name, so RequirementRefFallDetected also traces.
        ids += REQ_PATTERN.findall(name)
        if ids:
            traits[definition.get("id") or name] = sorted(set(ids))

    results = []
    for result in root.iterfind(".//t:UnitTestResult", TRX_NS):
        name = result.get("testName") or ""
        outcome = (result.get("outcome") or "").lower()
        key = result.get("testId") or name
        ids = traits.get(key) or REQ_PATTERN.findall(name)
        if ids:
            results.append((name, outcome, sorted(set(ids))))
    return results


def parse_junit(path: Path) -> list[tuple[str, str, list[str]]]:
    """Return (test name, outcome, requirement ids) for each testcase in JUnit XML."""
    root = ET.parse(path).getroot()
    results = []
    for case in root.iter("testcase"):
        name = f"{case.get('classname', '')}::{case.get('name', '')}".strip(":")
        haystack = " ".join(
            filter(
                None,
                [
                    case.get("name"),
                    case.get("classname"),
                    *(p.get("value") or "" for p in case.iter("property")),
                ],
            )
        )
        ids = sorted(set(REQ_PATTERN.findall(haystack)))
        if not ids:
            continue
        if case.find("failure") is not None or case.find("error") is not None:
            outcome = "failed"
        elif case.find("skipped") is not None:
            outcome = "skipped"
        else:
            outcome = "passed"
        results.append((name, outcome, ids))
    return results


def collect_results(results_dir: Path) -> list[tuple[str, str, list[str]]]:
    collected = []
    for path in sorted(results_dir.rglob("*")):
        try:
            if path.suffix.lower() == ".trx":
                collected += parse_trx(path)
            elif path.suffix.lower() == ".xml":
                collected += parse_junit(path)
        except ET.ParseError as exc:
            print(f"warning: could not parse {path}: {exc}", file=sys.stderr)
    return collected


def render(requirements: dict[str, Requirement], unknown: dict[str, list[str]]) -> str:
    total = len(requirements)
    covered = sum(1 for r in requirements.values() if r.status == "PASS")
    failing = [r for r in requirements.values() if r.status == "FAIL"]
    untested = [r for r in requirements.values() if r.status == "NO TEST"]

    lines = [
        "# Requirements traceability matrix",
        "",
        "> Generated by `tools/generate_traceability.py`. Do not edit by hand.",
        "",
        f"- Requirements: **{total}**",
        f"- Verified by a passing test: **{covered}**",
        f"- With a failing test: **{len(failing)}**",
        f"- With no test: **{len(untested)}**",
        "",
        "| Requirement | Comes from | Tests | Status |",
        "| --- | --- | --- | --- |",
    ]

    for req in requirements.values():
        names = [name for name, _ in req.tests]
        shown = "<br>".join(f"`{n}`" for n in names[:4]) or "_none_"
        if len(names) > 4:
            shown += f"<br>…and {len(names) - 4} more"
        lines.append(f"| **{req.req_id}** | {req.sources} | {shown} | {req.status} |")

    if untested:
        lines += [
            "",
            "## Requirements with no test",
            "",
            *[f"- {r.req_id}: {r.text}" for r in untested],
        ]

    if failing:
        lines += [
            "",
            "## Requirements with a failing test",
            "",
            *[f"- {r.req_id}: {', '.join(r.failing)}" for r in failing],
        ]

    if unknown:
        lines += [
            "",
            "## Tests tagged with an unknown requirement",
            "",
            "These tags do not match any requirement in the register. Either the",
            "requirement was removed or the tag has a typo.",
            "",
            *[f"- `{req_id}`: {', '.join(names)}" for req_id, names in unknown.items()],
        ]

    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--requirements", type=Path,
                        default=Path("docs/04-software-requirements.md"))
    parser.add_argument("--results", type=Path, default=Path("artifacts"))
    parser.add_argument("--out", type=Path, default=Path("docs/traceability.md"))
    parser.add_argument("--check", action="store_true",
                        help="exit 1 if any requirement lacks a passing test")
    args = parser.parse_args()

    if not args.requirements.is_file():
        print(f"error: requirements file not found: {args.requirements}", file=sys.stderr)
        return 1

    requirements = parse_requirements(args.requirements)
    if not requirements:
        print(f"error: no requirements found in {args.requirements}", file=sys.stderr)
        return 1

    unknown: dict[str, list[str]] = defaultdict(list)
    if args.results.is_dir():
        for name, outcome, ids in collect_results(args.results):
            for req_id in ids:
                if req_id in requirements:
                    requirements[req_id].tests.append((name, outcome))
                else:
                    unknown[req_id].append(name)
    else:
        print(f"warning: results directory not found: {args.results}", file=sys.stderr)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(render(requirements, dict(unknown)), encoding="utf-8")
    print(f"wrote {args.out}")

    gaps = [r.req_id for r in requirements.values() if r.status != "PASS"]
    if gaps:
        print(f"{len(gaps)} requirement(s) without a passing test: {', '.join(gaps)}",
              file=sys.stderr)
    if unknown:
        print(f"unknown requirement tags: {', '.join(unknown)}", file=sys.stderr)

    if args.check and (gaps or unknown):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
