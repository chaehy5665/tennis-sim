# Bounce impact data

This directory holds the collision-record contract of the bounce instruction document (section 8.2).

## Files

- manifest.json: sources with provenance, dataset entries, and the split assignment that is fixed before fitting.
- synthetic/records.jsonl: SYNTHETIC records generated from a published truth vector. Software verification only.
- synthetic/generation.json: generator parameters, seed, record count and record file hash.
- ../schemas/bounce-record.schema.json and ../schemas/interaction-profile.schema.json: published schemas.

## Evidence classes

MEASURED_RAW, PUBLISHED_MEASUREMENT, DIGITIZED_MEASUREMENT, NORMATIVE_CONSTRAINT, SYNTHETIC, ASSUMED_PRIOR.
A record keeps its class through loading, fitting, evaluation and reporting. Nothing relabels synthetic data,
a regulation range, or a model output as a measured impact.

## Record requirements

One JSON object per line. Required: recordId, datasetId, sourceId, sessionId, evidenceType, positionM[3],
normal[3] (unit), velocityBeforeMS[3], velocityAfterMS[3], angularVelocityBeforeRadS[3],
angularVelocityAfterRadS[3], observed (mask), velocityAfterStdDevMS[3], angularVelocityAfterStdDevRadS[3].
An unmeasured quantity stays zero and is declared false in the mask; an unmeasured incident spin is treated as
unknown, not as zero spin, and such a record is used for the normal response only.

## Adding measured data

1. Record the source (URL/DOI, access date, file SHA-256, original units, licence, redistribution permission) in manifest.json.
2. Digitise or measure the impacts into a record file; fill the extraction metadata and uncertainty per row.
3. Add the dataset entry with its evidence class and ball/surface binding.
4. Fix the group (session or experiment) to split assignment before fitting; groups are never split across sets.
5. Run: tennis-calibrate validate-data --dataset data/bounce/manifest.json
6. Run: tennis-calibrate fit --dataset data/bounce/manifest.json --config calibration/fit-config.json --out artifacts/calibration/run-001

Without measured records, fit exits 3 (DATA_INSUFFICIENT) and exports no profile. A synthetic fit requires the
explicit --synthetic flag and yields a DEV_ONLY, SYNTHETIC profile that is never a calibration claim.
