# Profiles

Interaction profiles with their evidence status. Coefficients belong to exactly one ball-surface combination
and are never shared across surfaces by a material rule.

| File | Evidence | Calibration status | Validation | Release |
|---|---|---|---|---|
| design-unc-v1.json | ASSUMED_PRIOR | UNCALIBRATED | NOT_RUN | DEV_ONLY |
| ball-type2-nominal.json | ASSUMED_PRIOR | not applicable (ball spec) | NOT_RUN | DEV_ONLY |

design-unc-v1.json is the built-in runtime profile of the impulse model: constant en = 0.74, constant
mu_eff = 0.1724 (chosen so the sliding impulse reproduces the previous tangential speed factor 0.88 at
vn = 8 m/s, vt = 20 m/s), beta = 0. It is an uncalibrated design assumption, is not surface specific, and must
not be presented as a measured court property. It is regenerated from the code with:

    dotnet run --project src/TennisSim.Calibration --no-build -- export-profile --built-in --out profiles/design-unc-v1.json

A test asserts that this file still matches the built-in profile hash, so the shipped file cannot drift from
the runtime default.

A fitted profile for the labelled synthetic dataset is written by fit into its run directory, for example
artifacts/calibration/run-001/profile.json with calibrationStatus FITTED_SYNTHETIC_ONLY and release DEV_ONLY.
There is no APPROVED profile in this repository, because no measured impact record exists. Fitted profiles are
exported with a content hash that the replay records, so a replay stays bound to the coefficients it was
produced with.
