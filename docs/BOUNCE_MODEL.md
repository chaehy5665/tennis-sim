# Ball bounce model (V1) and surface data contract

Status of this document: implementation reference for tennissim-mvp-3. It describes what the code does. It
does not claim that any court, ball or coefficient in this repository is measured.

## Coordinates and units

Engine frame: X court width, Y up, Z court length, origin at the net centre on the ground; metres, seconds,
kilograms, radians per second. The instruction document is written with z up; a true vector relabels
components and a polar vector (angular velocity) additionally flips sign, because that map has det = -1:

    (X, Y, Z) = (x, z, y)             true vectors: position, velocity
    omega_engine = -(w_x, w_z, w_y)    polar vectors: angular velocity

For a flat court the outward normal is n = (0, 1, 0) and the contact offset is r = -R n = (0, -R, 0). Mat3 in
Bounce/Frames.cs applies v' = A v and omega' = det(A) A omega, so a mirror correctly flips spin while leaving
velocity alone.

## Ball and surface data separation

- BallSpec: id, revision, mass, radius, inertia factor kappa (I = kappa m R^2), provenance, uncertainties. The
  built-in type2-nominal uses m = 0.0577 kg, R = 0.0335 m, kappa = 0.55 and is marked ASSUMED_PRIOR /
  nominal_not_measured; the Type 2 regulation range (56.0-59.4 g, 6.54-6.86 cm) is a normative constraint, not
  a sample measurement.
- BallCondition: temperature, measured pressure, batch and usage metadata. Absent values stay null.
- SurfaceDefinition: geometry, construction, state and profile bindings. hard, clay and grass are material
  labels and UI tags, never formula branches.
- SurfaceSample: the contact position and unit normal chosen before the collision, plus material, location and
  condition metadata. The runtime environment returns a flat, homogeneous sample; no per-bounce noise and no
  spatial field are enabled (the interface exists, the undocumented effect is off).
- InteractionProfile: the only place contact coefficients live, and they belong to a ball-surface combination.
  It carries model id, vref, the three response models, the evaluation domain, an optional lookup table,
  evidence class, calibration and validation status, release gate, dataset hash and run id.

## V1 impact equations (as implemented)

    r  = -R n
    P  = I3 - n n^T
    vn = dot(v-, n)                    approach requires -vn > normalApproachEpsilon
    vt = P v-
    ut = P (v- + cross(omega-, r))     friction direction follows contact slip, not centre speed

    sn = -vn, st = abs(vt), su = abs(ut)
    f1 = log(1 + sn/vref)
    f2 = st^2 / (sn^2 + st^2 + vref^2)
    f3 = log(1 + su/vref)

    en     = EvaluateNormalResponse(...)      constrained to [0, 1] in V1
    mu_eff = EvaluateFrictionResponse(...)    constrained to >= 0
    beta   = EvaluateTangentialResponse(...)  constrained to [0, 1]

    Jn = -(1 + en) m vn
    mt      = 1 / (1/m + R^2/I)
    q_goal  = (1 + beta) mt su
    q_limit = mu_eff Jn
    q       = min(q_goal, q_limit)
    Jt      = -q ut / su                      (zero when su <= slipEpsilon)

    v+     = v- + (Jn n + Jt)/m
    omega+ = omega- + cross(r, Jt)/I

Response models: Constant (M1), StateDependentSigmoid (M2/M3, sigmoid(a0 + a1 f1 + a2 f2) for en and
mu_max sigmoid(b0 + b1 f3) for mu), and Zero / ConstantBeta / StateDependentSigmoid for beta. vref = 10 m/s
and the feature definitions are design proposals, not measured natural laws.

active_impulse_limit classifies what bound produced Jt: NEAR_ZERO_SLIP, COULOMB_LIMITED,
TANGENTIAL_TARGET_LIMITED, TIE_WITHIN_TOLERANCE. It is not a claim about the real contact history.
beta_effective = -dot(ut+, ut-)/abs(ut-)^2 is reported separately and can be negative when slip survives; it
is never confused with the input beta_grip.

## Contact states that are not impulses

| Status | Meaning | Effect |
|---|---|---|
| RESOLVED | approach impulse applied | post state from the equations above |
| SETTLED | resolved normal speed below settleNormalSpeedMS (0.02 m/s, a rise below 20 micrometres) | resting contact: no impulse, normal component removed, tangential velocity and spin kept, warning SETTLED_CONTACT |
| SEPARATING_NO_IMPULSE | already moving away | no impulse |
| TANGENTIAL_CONTACT_NO_IMPULSE | abs(vn) inside the approach tolerance | no impulse |
| DEEP_INITIAL_PENETRATION | clearance below -deepPenetrationEpsilonM | no impulse, no position change |
| NUMERICAL_FAILURE | non-finite output or an invariant violation beyond tolerance | reported, never silently corrected |

Structural problems (NaN mass, non-positive radius, unnormalized normal, en outside [0,1], beta outside [0,1],
non-finite impact state, negative tolerance) raise ArgumentException. They are never converted into a
plausible-looking bounce.

The SETTLED state exists because a rigid point-mass model with restitution plus gravity otherwise produces an
endless series of ever smaller bounces inside one step. The policy is explicit, per-unit, and published in the
event diagnostics; it is not a measurement. Rolling resistance is not modelled: a settled ball keeps its
tangential velocity and spin.

## Invariants checked in code and in tests

    E_after <= E_before + atol + rtol max(E_before, E_after)
    Jn >= 0
    abs(Jt) <= mu_eff Jn + impulseTolerance
    abs(dot(Jt, n)) <= tangentTolerance
    I (omega+ - omega-) = cross(r, Jt)
    dot(omega+ - omega-, n) = 0                        (no invented side-spin kick)
    dot(p+ - contact, n) = R                           (contact point on the surface)
    dot(Jt, ut-) + abs(Jt)^2/(2 mt) <= 0               (tangential energy change)

Tolerances are separate per unit (BounceTolerances): normal approach and slip in m/s, penetration in m,
impulses in N s, energy in J, tie comparison dimensionless, settle speed in m/s. PhysicsEpsilons holds the
integration-level length and time epsilons.

## Integration into the flight and the event stream

Per ground contact inside one step: the bisection finds t*, the pre-state is evaluated at t*, one SurfaceSample
is taken, ResolveBounce is called exactly once, the post-state is applied, Bounces increments and the
remaining flight integrates. A contact that produces no impulse does not create a bounce event; it holds the
ball on the surface and ends the step. If one step still contains more than twelve contacts the engine raises
SimulationLimitExceeded with a diagnostic instead of looping.

Movement.PredictContact receives the same environment, so the receiver prediction and the engine flight use one
model.

## Event and replay additions

BallBounced events carry an optional bounce object: post state, impulses, pre/post contact slip, the used en,
mu_eff and beta, beta_effective, the active limit, energies, profile id/revision/hash, model id, outOfDomain,
status and warnings. MatchInput.Surface carries the ball spec, condition, profile and tolerances, so a replay
is self-describing. Both fields are omitted when absent, which is why a legacy replay serialises to the same
bytes as before apart from the engine tag.

## Known limitations

- Stationary local plane, point-mass ball: no finite contact patch, no deformation history, no surface motion.
- No air spin effects (Magnus) and no spin decay in flight; spin is carried unchanged.
- No rolling resistance, no directional/loam/wear/humidity effect, no shoe-court coupling.
- OUT_OF_DOMAIN bounds only the parameter evaluation point; the physical impulse still uses the real inputs.
- The lookup table is a lossy runtime representation with pre-registered comparison tolerances; matching the
  analytic evaluator is not bitwise reproducibility.
- No coefficient in this repository is measured. See reports/model-card.md and reports/calibration-report.md.
