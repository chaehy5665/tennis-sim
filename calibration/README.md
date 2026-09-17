# Calibration configuration

- fit-config.json: the pre-registered fitting configuration used by fit. Every field has a default in
  FitConfig; the file records the values actually used, and fit copies it into run.json.

Field groups:

- Model and splits: model, trainSplit, validationSplit, testSplit.
- Feature normalisation: vrefMS.
- Loss: huberDelta, priorLambda.
- Bounds: muMaxCap, parameterBoundAbs.
- Record filters: normalApproachFloorMS (normal-response fitting), angleFloorTangentialMS (angle metric
  exclusion).
- Optimiser: maxEvaluations, initialStep, shrink, stopStep, extraStarts.
- Bootstrap: bootstrapResamples, bootstrapSeed, bootstrapStopStep.
- Model selection: validationRmseGainForComplexity.
- Targets: targets (normal m/s, tangential m/s, angular rpm, exit angle deg, CPR points).

Changing a value changes a pre-registered decision, so it belongs in a new run with a new output directory:
fit always writes hashes of the dataset, manifest, splits and exported profile into run.json.

- fit-config-cross2002.json: configuration for the published-measurement dataset. No bootstrap (one independent
  training group), a lower normal-approach floor for the low-speed bounces, and M1/M1B candidates.
