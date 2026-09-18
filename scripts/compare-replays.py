#!/usr/bin/env python3
"""Compare two TennisSim replays field by field.

Used for cross-platform checks: the same input, produced on two machines, is compared leaf by leaf.
Numeric leaves use abs(a-b) <= atol + rtol*max(abs(a),abs(b)); non-numeric leaves must be equal after the
engine version string is normalised (a physics version change is a deliberate difference, not noise).

Exit codes: 0 within tolerance, 1 differences beyond tolerance or structural differences, 2 usage or file error.
"""
import argparse, hashlib, json, pathlib, sys

def leaves(node, path=""):
    if isinstance(node, dict):
        for key, value in node.items():
            yield from leaves(value, path + "/" + str(key))
    elif isinstance(node, list):
        for index, value in enumerate(node):
            yield from leaves(value, path + "/" + str(index))
    else:
        yield path, node

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("left")
    parser.add_argument("right")
    parser.add_argument("--atol", type=float, default=1e-7)
    parser.add_argument("--rtol", type=float, default=1e-7)
    parser.add_argument("--sample-limit", type=int, default=8)
    parser.add_argument("--out", help="optional path for the JSON report")
    args = parser.parse_args()

    left_path, right_path = pathlib.Path(args.left), pathlib.Path(args.right)
    for path in (left_path, right_path):
        if not path.is_file():
            print("ERROR: replay not found: " + str(path), file=sys.stderr)
            return 2
    left_bytes, right_bytes = left_path.read_bytes(), right_path.read_bytes()
    left, right = json.loads(left_bytes), json.loads(right_bytes)

    report = {
        "left": str(left_path),
        "right": str(right_path),
        "leftSha256": hashlib.sha256(left_bytes).hexdigest(),
        "rightSha256": hashlib.sha256(right_bytes).hexdigest(),
        "byteIdentical": left_bytes == right_bytes,
        "assertions": {},
    }

    def assertion(name, ok, detail=None):
        entry = {"pass": bool(ok)}
        if detail is not None:
            entry["detail"] = detail
        report["assertions"][name] = entry

    version_left = left.get("engineVersion")
    version_right = right.get("engineVersion")
    assertion("engineVersionEqual", version_left == version_right, {"left": version_left, "right": version_right})
    if version_left != version_right:
        left = dict(left)
        left["engineVersion"] = version_right

    structural = {
        "eventCount": (len(left.get("events", [])), len(right.get("events", []))),
        "frameCount": (len(left.get("frames", [])), len(right.get("frames", []))),
        "eventKinds": ([e.get("kind") for e in left.get("events", [])], [e.get("kind") for e in right.get("events", [])]),
        "eventSequences": ([e.get("sequence") for e in left.get("events", [])], [e.get("sequence") for e in right.get("events", [])]),
        "finalScore": (left.get("finalScore", {}).get("display"), right.get("finalScore", {}).get("display")),
        "status": (left.get("status"), right.get("status")),
        "finalRandomState": (left.get("finalRandomState"), right.get("finalRandomState")),
        "input": (left.get("input"), right.get("input")),
    }
    assertion("eventCountEqual", structural["eventCount"][0] == structural["eventCount"][1], structural["eventCount"])
    assertion("frameCountEqual", structural["frameCount"][0] == structural["frameCount"][1], structural["frameCount"])
    assertion("eventKindOrderEqual", structural["eventKinds"][0] == structural["eventKinds"][1])
    assertion("eventSequenceEqual", structural["eventSequences"][0] == structural["eventSequences"][1])
    assertion("finalScoreEqual", structural["finalScore"][0] == structural["finalScore"][1], structural["finalScore"][0])
    assertion("statusEqual", structural["status"][0] == structural["status"][1], structural["status"][0])
    assertion("finalRandomStateEqual", structural["finalRandomState"][0] == structural["finalRandomState"][1], structural["finalRandomState"])
    assertion("inputEqual", structural["input"][0] == structural["input"][1])

    left_leaves, right_leaves = dict(leaves(left)), dict(leaves(right))
    keys = set(left_leaves) | set(right_leaves)
    numeric_diffs, non_numeric_diffs = 0, []
    max_abs, max_rel, first_numeric = 0.0, 0.0, None
    for key in sorted(keys):
        a, b = left_leaves.get(key, "<missing>"), right_leaves.get(key, "<missing>")
        if a == b:
            continue
        if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool) and not isinstance(b, bool):
            difference = abs(a - b)
            scale = max(abs(a), abs(b), 1e-300)
            if difference > args.atol + args.rtol * scale:
                numeric_diffs += 1
                if first_numeric is None:
                    first_numeric = {"path": key, "left": a, "right": b, "absDelta": difference}
            max_abs = max(max_abs, difference)
            max_rel = max(max_rel, difference / scale)
        else:
            if len(non_numeric_diffs) < args.sample_limit:
                non_numeric_diffs.append({"path": key, "left": repr(a)[:80], "right": repr(b)[:80]})

    report.update({
        "leafCountLeft": len(left_leaves),
        "leafCountRight": len(right_leaves),
        "numericLeavesBeyondTolerance": numeric_diffs,
        "maxAbsDelta": max_abs,
        "maxRelDelta": max_rel,
        "firstNumericDifference": first_numeric,
        "nonNumericDifferenceCount": sum(1 for key in keys if left_leaves.get(key, "<missing>") != right_leaves.get(key, "<missing>")
                                          and not (isinstance(left_leaves.get(key), (int, float)) and isinstance(right_leaves.get(key), (int, float)))),
        "nonNumericDifferenceSample": non_numeric_diffs,
        "atol": args.atol,
        "rtol": args.rtol,
    })
    assertion("numericLeavesWithinTolerance", numeric_diffs == 0, {"beyondTolerance": numeric_diffs, "maxAbsDelta": max_abs})
    assertion("nonNumericLeavesEqual", report["nonNumericDifferenceCount"] == 0, report["nonNumericDifferenceSample"])

    failed = [name for name, entry in report["assertions"].items() if not entry["pass"]]
    report["result"] = "IDENTICAL_BYTES" if report["byteIdentical"] else ("WITHIN_TOLERANCE" if not failed else "DIFFERENCES")
    report["failedAssertions"] = failed
    text = json.dumps(report, indent=2)
    if args.out:
        pathlib.Path(args.out).write_text(text + "\n")
    print(text)
    if report["byteIdentical"]:
        print("REPLAY_COMPARE byte-identical sha256=" + report["leftSha256"])
    else:
        print("REPLAY_COMPARE " + report["result"] + " numericBeyondTolerance=" + str(numeric_diffs)
              + " maxAbsDelta=" + repr(max_abs) + " failed=" + ",".join(failed) if failed else
              "REPLAY_COMPARE " + report["result"] + " numericBeyondTolerance=0 maxAbsDelta=" + repr(max_abs))
    return 0 if report["result"] in ("IDENTICAL_BYTES", "WITHIN_TOLERANCE") else 1

if __name__ == "__main__":
    sys.exit(main())
