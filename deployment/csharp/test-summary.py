#!/usr/bin/env python3
"""Write real test counters and per-assembly coverage to the GitHub job summary."""
import os
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1])
lines = ['## Test results', '', '| Result file | Total | Passed | Failed |', '| --- | ---: | ---: | ---: |']
for path in sorted(root.rglob('*.trx')):
    document = ET.parse(path)
    counters = document.find('.//{*}Counters')
    if counters is not None:
        lines.append(f"| {path.name} | {counters.get('total')} | {counters.get('passed')} | {counters.get('failed')} |")
lines += ['', '## Line coverage by test project', '', '| Assembly | Line coverage |', '| --- | ---: |']
for path in sorted(root.rglob('coverage.cobertura.xml')):
    for package in ET.parse(path).findall('.//package'):
        rate = float(package.get('line-rate', '0')) * 100
        lines.append(f"| {package.get('name')} ({path.parent.name[:8]}) | {rate:.1f}% |")
lines += ['', 'Coverage from different test projects is shown separately, not added together.', '']
text = '\n'.join(lines)
print(text)
if output := os.environ.get('GITHUB_STEP_SUMMARY'):
    with open(output, 'a') as summary:
        summary.write(text)
