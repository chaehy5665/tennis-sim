# Integration notes — court surface and ball bounce model (2026-09-15)

Pre-implementation survey required by section 1 of the bounce instruction document. Read-only survey;
no file was modified to produce this note.

## Repository baseline

- Baseline HEAD: 093be0f (Fix court corner contact classification) with a dirty work tree that already
  contained the 2026-09-14 realism-audit evidence (docs, viewer changes, calibration scripts). Those changes
  were preserved untouched.
- Existing engine identification: tennissim-mvp-2, replay schema 1.0.
- Toolchain verified in this run: .NET SDK 10.0.401 / runtime 10.0.12, Linux x64, Core netstandard2.1 / C# 8.0,
  CLI, tests and the new calibration tool net10.0.
- Build and test commands: dotnet build TennisSim.sln, dotnet run --project tests/TennisSim.Tests (an
  executable checker, not dotnet test), dotnet run --project tests/TennisSim.ViewerChecks -- <replay>.

## Where each required element lived before this change

| Element | Location |
|---|---|
| Ball state and coordinates | src/TennisSim.Core/Physics.cs (BallState), Geometry.cs (Vec3, Court) |
| Ball specification | none. Radius was a constant (Court.BallRadius = 0.0335); mass and inertia did not exist |
| Air trajectory integrator | BallPhysics.At, analytic dv/dt = -k v + (0,-g,0) solution |
| Collision-time finder | BallPhysics.Root, 40-step bisection inside one step |
| Existing bounce formula | BallPhysics.Advance, ground branch: vx *= 0.88, vy = abs(vy) * 0.74, vz *= 0.88, no spin |
| Surface presets | none. hard/clay/grass did not exist anywhere in code |
| BallBounced event | Collision (Physics.cs) projected into MatchEvent by MatchEngine.StepBall |
| RNG, seed, replay | SeedRandom (xorshift32), MatchInput.Seed, ReplayJson, docs/REPLAY_CONTRACT.md |
| Physics version and config hash | MatchRecord.EngineVersion, MatchInput.Config, Diagnostics.Hash(config) |
| Renderer transform and interpolation | unity/TennisSim.UnityViewer/.../Runtime/Data (identity coordinate mapping) |
| Platform regression tests | tests/TennisSim.Tests, tests/TennisSim.ViewerChecks, src/TennisSim.Cli scenarios |

Coordinate system found and preserved: X court width, Y up, Z court length, origin at the net centre on the
ground. The instruction document is written with z up, so its equations were transcribed into the engine
frame: a true vector (position, velocity) relabels components (X,Y,Z) = (x, z, y); a polar vector (angular
velocity) takes the additional det = -1 sign, omega_engine = -(w_x, w_z, w_y). This mapping was verified
against independently recomputed document fixtures (see docs/BOUNCE_MODEL.md).

## Decisions taken

1. **Model selection is explicit and opt-in.** MatchInput.Surface is null for the existing multiplicative
   bounce, so every existing replay, fixture and documented number stays valid; --surface-model impulse in the
   CLI selects the explicit V1 impulse model. Both paths run through the same collision pipeline.
2. **One implementation of the collision model.** BounceModel.Resolve is a pure function in Core, used by the
   runtime, by the predictor (Movement.PredictContact) and by the calibration tool. There is no second,
   independent forward model to drift out of sync.
3. **Coefficients belong to a ball-surface combination.** BallSpec (mass, radius, kappa) and SurfaceDefinition
   (geometry, construction, state) are separate from InteractionProfile (normal, friction, tangential
   response). No material-combination rule such as e_ball x e_surface was invented.
4. **No invented measurements.** The runtime default profile is labelled UNCALIBRATED, ASSUMED_PRIOR,
   DEV_ONLY. fit exits 3 without measured records and exports no profile; the labelled synthetic dataset only
   runs with the explicit --synthetic flag.
5. **Physics version.** EngineVersion is now tennissim-mvp-3. Schema 1.0 is unchanged. The legacy path is
   numerically identical to the preserved tennissim-mvp-2 candidate replay (0 of 585057 leaf differences after
   normalising the version string); the impulse model is a new physics version, so resimulate refuses older
   replays and the viewer accepts v1, v2 and v3.
6. **Player ability, tactics, scoring and the in/out and second-bounce rules were not changed.** The renderer
   still never determines an outcome; it only interpolates and displays.

## Integration points changed

- src/TennisSim.Core/Bounce/* (new): contracts, model, profiles, frames.
- src/TennisSim.Core/Physics.cs: BallState.AngularVelocity, Collision.Bounce, PhysicsEpsilons, ground contact
  routing, settling policy.
- src/TennisSim.Core/Contracts.cs: MatchInput.Surface, MatchEvent.Bounce, engine version.
- src/TennisSim.Core/MatchEngine.cs and Players.cs: surface environment threaded through flight and prediction.
- src/TennisSim.Cli: bounce command, --surface-model/--profile/--ball/--ball-condition, bounce scenarios,
  optional-field serialization policy.
- src/TennisSim.Calibration (new): dataset loading, staged fitting, evaluation, virtual ITF-shaped test, table
  export and table-versus-analytic comparison.
- data/bounce, calibration, schemas, reports (new): data contract, configuration, schemas, reports.
- unity/TennisSim.UnityViewer/.../ReplayLoader.cs: accepts engine v3. Tests/Shared/ReplayChecks.cs: the
  required-key anchor for the state ball field no longer assumes that input.surface.ball does not exist.
