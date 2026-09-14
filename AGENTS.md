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