#!/usr/bin/env python3
"""Summarize a `balance` grid into a Markdown report.

Usage: python3 scripts/summarize-balance.py artifacts/balance/balance-2000.json [--out report.md]

Answers three questions:
  1. Per player matchup, how much does A's tactic change A's point win rate, and which tactic is best?
  2. Is one tactic best in every matchup (a dominant, "solved" choice)?
  3. In the mirror payoff matrix, does each opponent tactic have a different best response (a counter structure)?
Win rates exclude points that reached the rally limit; the limit rate is reported next to them.
"""
import argparse
import json
import sys


def pct(x):
    return f"{100 * x:.1f}"


def limit_rate(c):
    """Share of points that ended at the rally limit (each limit ends one point and its run)."""
    total = c["points"] + c["rallyLimits"]
    return c["rallyLimits"] / total if total else 0.0


def significantly_different(a, b):
    """Non-overlapping 95% Wilson intervals: a conservative test for a real difference."""
    return a["wilsonLow"] > b["wilsonHigh"] or b["wilsonLow"] > a["wilsonHigh"]


def fictitious_play(matrix, rounds=20000):
    """Approximate mixed equilibrium of the zero-sum game where the row player maximizes matrix[i][j]."""
    n, m = len(matrix), len(matrix[0])
    row_counts, col_counts = [0] * n, [0] * m
    row_payoff, col_payoff = [0.0] * n, [0.0] * m
    i, j = 0, 0
    for _ in range(rounds):
        row_counts[i] += 1
        col_counts[j] += 1
        for r in range(n):
            row_payoff[r] += matrix[r][j]
        for c in range(m):
            col_payoff[c] += matrix[i][c]
        i = max(range(n), key=lambda r: row_payoff[r])
        j = min(range(m), key=lambda c: col_payoff[c])
    return [x / rounds for x in row_counts], [x / rounds for x in col_counts]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input")
    parser.add_argument("--out")
    args = parser.parse_args()
    try:
        with open(args.input) as f:
            data = json.load(f)
    except (OSError, json.JSONDecodeError) as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 2

    cells = data["cells"]
    out = []
    w = out.append
    w(f"# Tactic balance diagnosis\n")
    runs = f", runs of {data['runPoints']} consecutive points" if "runPoints" in data else ""
    w(f"Source: `{args.input}`, engine {data['engineVersion']}, {data['count']} points per cell{runs}, "
      f"initial seed {data['initialSeed']} (shared by every cell). Win rate = A point win rate excluding rally-limit points; "
      f"95% Wilson interval in brackets. REALISM_CALIBRATED=false.\n")

    # 1 + 2: vs balanced opponent
    vs = [c for c in cells if c["experiment"] == "vs-balanced"]
    matchups = []
    for c in vs:
        key = (c["playerA"], c["playerB"])
        if key not in matchups:
            matchups.append(key)
    w("## 1. A tactic against a Balanced opponent, per matchup\n")
    best_by_matchup = {}
    spreads = []
    for a, b in matchups:
        rows = [c for c in vs if (c["playerA"], c["playerB"]) == (a, b)]
        best = max(rows, key=lambda c: c["winRateA"])
        worst = min(rows, key=lambda c: c["winRateA"])
        tied = [c["tacticA"] for c in rows if not significantly_different(c, best)]
        best_by_matchup[(a, b)] = (best["tacticA"], tied)
        spreads.append((a, b, best["winRateA"] - worst["winRateA"]))
        w(f"### A={a} vs B={b}\n")
        w("| A tactic | win % | 95% CI | A serving % | A returning % | shots/pt | rally limit % |")
        w("|---|---:|---|---:|---:|---:|---:|")
        for c in sorted(rows, key=lambda c: -c["winRateA"]):
            sv = c["serveWonA"] / c["servePoints"] if c["servePoints"] else 0
            rt = c["returnWonA"] / c["returnPoints"] if c["returnPoints"] else 0
            mark = " **best**" if c is best else (" (tied)" if c["tacticA"] in tied else "")
            w(f"| {c['tacticA']}{mark} | {pct(c['winRateA'])} | {pct(c['wilsonLow'])}-{pct(c['wilsonHigh'])} | "
              f"{pct(sv)} | {pct(rt)} | {c['meanShots']:.1f} | {pct(limit_rate(c))} |")
        w(f"\nSpread best-worst: {pct(best['winRateA'] - worst['winRateA'])} percentage points. "
          f"Statistically tied with best: {', '.join(tied)}.\n")

    w("## 2. Is one tactic best everywhere?\n")
    w("| matchup | best | tied with best | spread (pp) |")
    w("|---|---|---|---:|")
    for a, b, spread in spreads:
        best, tied = best_by_matchup[(a, b)]
        w(f"| {a} vs {b} | {best} | {', '.join(tied)} | {pct(spread)} |")
    always = set.intersection(*(set(t) for _, t in best_by_matchup.values()))
    w("")
    if always:
        w(f"**DOMINANT**: {', '.join(sorted(always))} is statistically tied for best in every matchup. "
          f"Choosing a tactic by opponent adds nothing there.\n")
    else:
        w("**NO SINGLE DOMINANT TACTIC**: the best tactic depends on the matchup.\n")

    # 3: mirror payoff
    mirror = [c for c in cells if c["experiment"] == "mirror-payoff"]
    if mirror:
        names = []
        for c in mirror:
            if c["tacticA"] not in names:
                names.append(c["tacticA"])
        lookup = {(c["tacticA"], c["tacticB"]): c for c in mirror}
        matrix = [[lookup[(r, col)]["winRateA"] for col in names] for r in names]
        w("## 3. Mirror payoff (baseline vs baseline): A win % by A tactic (row) and B tactic (column)\n")
        w("| A \\ B | " + " | ".join(names) + " |")
        w("|---|" + "---:|" * len(names))
        for r, row in zip(names, matrix):
            w(f"| {r} | " + " | ".join(pct(x) for x in row) + " |")
        w("\nRally limit % per cell:\n")
        w("| A \\ B | " + " | ".join(names) + " |")
        w("|---|" + "---:|" * len(names))
        for r in names:
            w(f"| {r} | " + " | ".join(pct(limit_rate(lookup[(r, col)])) for col in names) + " |")
        w("\nBest response of A to each B tactic:\n")
        responses = set()
        tied_sets = []
        for j, col in enumerate(names):
            i = max(range(len(names)), key=lambda i: matrix[i][j])
            responses.add(names[i])
            best_cell = lookup[(names[i], col)]
            tied = [r for r in names if r != names[i] and not significantly_different(lookup[(r, col)], best_cell)]
            tied_sets.append({names[i], *tied})
            w(f"- B {col}: A {names[i]} ({pct(matrix[i][j])} %)" + (f"; tied: {', '.join(tied)}" if tied else ""))
        robust = set.intersection(*tied_sets)
        if robust:
            w(f"\nTied for best against every B tactic: {', '.join(sorted(robust))}. "
              f"A different nominal best response per column is then noise, not a counter structure.")
        dominant_rows = [names[i] for i in range(len(names))
                         if all(matrix[i][j] >= matrix[k][j] for j in range(len(names)) for k in range(len(names)))]
        row_mix, col_mix = fictitious_play(matrix)
        w("")
        if dominant_rows:
            w(f"**DOMINANT STRATEGY**: {', '.join(dominant_rows)} is the best response to every B tactic. No counter structure.\n")
        elif robust:
            w(f"**NO SIGNIFICANT COUNTER STRUCTURE**: nominal best responses differ, but {', '.join(sorted(robust))} "
              f"is never significantly worse than the best.\n")
        elif len(responses) == 1:
            w(f"**SINGLE BEST RESPONSE** {responses.pop()} to every column. No counter structure.\n")
        else:
            w(f"**COUNTER STRUCTURE PRESENT**: {len(responses)} different best responses.\n")
        w("Approximate equilibrium mix (fictitious play), row player: " +
          ", ".join(f"{n} {pct(p)}%" for n, p in zip(names, row_mix) if p > 0.005) + "\n")

    text = "\n".join(out)
    if args.out:
        with open(args.out, "w") as f:
            f.write(text + "\n")
        print(f"REPORT={args.out}")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
