# Tennis Simulator Project Instructions

This repository is an isolated tennis simulation project.

## Scope

- Work only inside this repository unless explicitly asked otherwise.
- Do not inspect or modify Vocado repositories or Vocado documentation.
- Do not reuse Vocado-specific architecture, naming, product assumptions, or workflows.
- Treat this repository as an independent project.

## Product

The goal is a tactical tennis management simulation.

Long-term presentation:
- 3D TV-broadcast-style match viewing
- manager/tactical gameplay
- simulation-driven match outcomes

Current priority:
1. deterministic tennis simulation core
2. rules and scoring
3. player movement and shot decision model
4. tactics
5. replay/event contract
6. simple debug visualization
7. 3D presentation later

## Architecture

Keep simulation independent of rendering.

Preferred boundary:

TennisSim.Core
    ↓
Simulation events / state
    ↓
Presentation adapters
    ↓
Unity or other renderer

The renderer must never determine scoring or match outcomes.

## Engineering

- Prefer simple, testable implementations.
- Avoid premature abstraction.
- Add tests for simulation invariants.
- Use explicit seeded randomness.
- Preserve deterministic replay where practical.
- Keep Unity dependencies out of the simulation core.
- Do not introduce network or cloud dependencies without approval.

## Git

- Inspect git status before editing.
- Do not overwrite unrelated user changes.
- Keep changes scoped to the current task.
- Do not push remotely unless explicitly requested.

## Validation

Before declaring work complete:

- build
- run relevant tests
- run at least one deterministic sample simulation
- report remaining limitations honestly

## Models and delegation

Decided by the user on 2026-09-25.

- **Opus 5.5** runs every team session (leader, Debug, UI, Design, Character, Mac). It owns decisions, Core and engine
  changes, reviews, merges and final verification.
- **deepseek-v4-1-flash** (agent `deepseek-v4-1-flash`, or `ui-builder` for UI implementation) takes scoped work that an
  Opus session delegates: searching the code, running measurements and summarising the numbers, mechanical edits in the
  files the delegating session names, and test scaffolding. It makes no design or balance decisions.
- **muse-spark-1-3-contributor** (agent `muse-spark-1-3-contributor`, or `ui-qa`) does read-only review: checklists,
  docs, diffs and screenshots. It never edits.

Rules for delegation:

- The delegating Opus session checks everything a delegated agent returns and runs the usual validation (build, tests,
  seed 42 bytes) before committing. Delegated agents never commit, push or merge.
- Give a delegated agent the exact worktree and files to touch. The generated agent prompts forbid creating files in
  their working directory, so name any new file explicitly.
- Never put secrets in a delegated prompt.
- These two external models are reached through the local ClaudeRipple proxy (opencode-go), so repository content is
  sent to that provider. The user approved this for these two models only. Other external models (for example the
  `ocx-*` agents) are not approved; ask before using them.
