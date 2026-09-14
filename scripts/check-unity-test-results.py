#!/usr/bin/env python3
"""Fail closed on empty, incomplete, skipped or failed Unity NUnit results."""
import sys
import xml.etree.ElementTree as ET
try:
    root = ET.parse(sys.argv[1]).getroot()
    cases = list(root.iter('test-case'))
    passed = sum(case.get('result') == 'Passed' for case in cases)
    total = int(root.get('total', '0'))
    assert root.tag == 'test-run' and root.get('result') == 'Passed'
    assert root.get('start-time') and root.get('end-time')
    assert total > 0 and len(cases) == total and passed == total
    assert int(root.get('failed', '-1')) == 0
    assert int(root.get('passed', '-1')) == total
except (OSError, ET.ParseError, AssertionError, ValueError, IndexError) as exc:
    sys.exit('ERROR: Missing, empty, failed or incomplete Unity test XML: ' + str(exc))
print(f'UNITY_TEST_XML={sys.argv[1]} total={total} passed={passed} failed=0 complete=true')
