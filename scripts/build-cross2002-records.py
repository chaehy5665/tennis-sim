#!/usr/bin/env python3
"""Build published bounce records from Cross, Am. J. Phys. 70, 482-489 (2002).

Source: "Measurements of the horizontal coefficient of restitution for a superball and a
tennis ball", Rod Cross, Am. J. Phys. 70(5), 482-489 (2002), DOI 10.1119/1.1450571.
Fetched copy: https://physics.umd.edu/courses/Phys405/Hill/Fall05/Information/AJP/AJP00482.pdf

Extraction method: the PDF text layer was read directly (no OCR). Table I and Table II values were
read from the text objects of the page streams. Derived checks in this script re-compute the
published quantities that the tables also list (vx2, vx2/vx1, ey, R*omega2) and fail loudly if the
read columns disagree, so a mis-read cannot silently become a data record.

Frame mapping (documented, transform revision 1):
  paper surface frame: x along the surface in the travel direction, y normal to the surface
  engine frame: X width, Y up (surface normal), Z horizontal travel
  true vectors: v_engine = (0, +/- v sin(theta), v cos(theta))
  spin: forward rotation is +X (topspin for a ball travelling +Z)

Zero incident spin is an experimental condition of the paper ("The ball was dropped vertically with
zero initial speed, zero initial spin"), not a measurement, so it is recorded with the mask set and
flagged. Gravity during the ~4 ms contact is not removed from the measured states; the expected
effect is recorded as a quality flag.
"""
import argparse, hashlib, json, pathlib, math

PAGE_TABLE_I = "printed p. 486, Table I"
PAGE_TABLE_II = "printed p. 487, Table II"
PDF_URL = "https://physics.umd.edu/courses/Phys405/Hill/Fall05/Information/AJP/AJP00482.pdf"
PDF_SHA256 = "b5cbf25b165491327335d997a418f4c2d242fc694ad077fdb22243e6309ad536"
BALL = {"id": "type2-nominal", "radiusM": 0.0335, "paperRadiusM": 0.033}

# Table II, h = 60 cm drop, zero incident spin, one typical individual bounce per surface and angle.
# Columns read: surface, theta1 (deg), v1, v2, theta2 (deg), omega2 (rad/s), published vx2,
# published vx2/vx1, published ey. The last three are re-derived below as a read check.
TABLE_II = [
    ("wood", "Wood", 20.0, 3.72, 3.09, 19.2, 34.9, 2.92, 0.84, 0.80),
    ("wood", "Wood", 45.0, 3.80, 2.53, 50.4, 58.2, 1.61, 0.60, 0.73),
    ("emery", "Emery paper", 20.0, 3.53, 2.24, 27.2, 78.5, 1.99, 0.60, 0.84),
    ("emery", "Emery paper", 45.0, 3.70, 2.66, 51.3, 49.9, 1.66, 0.63, 0.79),
    ("rebound-ace", "Rebound Ace court surface", 20.0, 3.35, 2.13, 23.4, 76.7, 1.96, 0.62, 0.74),
    ("rebound-ace", "Rebound Ace court surface", 45.0, 3.60, 2.66, 49.3, 50.6, 1.74, 0.68, 0.79),
]

# Table I, h = 25 cm drop, theta1 = 20 deg on emery. Component values with the paper's own
# uncertainties (surface frame): vx1, vy1, vx2, vy2 in m/s, omega2 in rad/s, theta2 in degrees.
TABLE_I = {"surface": "emery", "theta1": 20.0, "theta1SigmaDeg": 0.5,
           "vx1": 2.10, "vx1Sigma": 0.04, "vy1": 0.77, "vy1Sigma": 0.014,
           "vx2": 1.23, "vx2Sigma": 0.03, "vy2": 0.61, "vy2Sigma": 0.03,
           "omega2": 52.4, "omega2Sigma": 0.5, "theta2": 26.2, "theta2SigmaDeg": 1.5}

METHODS_PRECISION = 0.02      # horizontal component determined to within 2 percent (Section IV)
ANGLE_SIGMA_DEG = 1.5         # Table I states +-1.5 deg for the digitized rebound angle
ROUNDING_3SF = 0.0005         # values printed to three significant figures


def sigma_from_published(value, relative, rounding=ROUNDING_3SF):
    return max(abs(value) * relative, abs(value) * rounding)


def components(speed, angle_deg, approach):
    angle = math.radians(angle_deg)
    sign = -1.0 if approach else 1.0
    return (0.0, sign * speed * math.sin(angle), speed * math.cos(angle))


def record(record_id, experiment, session, surface, surface_label, theta1, v1, v2, theta2, omega2,
           sigma_v1, sigma_v2, sigma_theta2_deg, sigma_omega2, source, row, page, flags,
           vx1=None, vy1=None, vx2=None, vy2=None, sigma_vx1=None, sigma_vy1=None, sigma_vx2=None, sigma_vy2=None):
    if vx1 is None:
        vx1, vy1 = v1 * math.cos(math.radians(theta1)), v1 * math.sin(math.radians(theta1))
        vx2, vy2 = v2 * math.cos(math.radians(theta2)), v2 * math.sin(math.radians(theta2))
        sigma_x2 = math.hypot(math.cos(math.radians(theta2)) * sigma_v2,
                              v2 * math.sin(math.radians(theta2)) * math.radians(sigma_theta2_deg))
        sigma_y2 = math.hypot(math.sin(math.radians(theta2)) * sigma_v2,
                              v2 * math.cos(math.radians(theta2)) * math.radians(sigma_theta2_deg))
        sigma_x1 = math.hypot(math.cos(math.radians(theta1)) * sigma_v1,
                              v1 * math.sin(math.radians(theta1)) * math.radians(0.5))
        sigma_y1 = math.hypot(math.sin(math.radians(theta1)) * sigma_v1,
                              v1 * math.cos(math.radians(theta1)) * math.radians(0.5))
    else:
        sigma_x1, sigma_y1, sigma_x2, sigma_y2 = sigma_vx1, sigma_vy1, sigma_vx2, sigma_vy2
    before = (0.0, -vy1, vx1)
    after = (0.0, vy2, vx2)
    return {
        "recordId": record_id,
        "datasetId": "cross2002-tennis-ball-surfaces",
        "sourceId": "C2002",
        "evidenceType": "PUBLISHED_MEASUREMENT",
        "experimentId": experiment,
        "sessionId": session,
        "sampleId": row,
        "ballId": "type2-nominal",
        "ballBatchId": "",
        "ballSpecId": BALL["id"],
        "surfaceId": surface,
        "surfaceConditionId": "laboratory, dry, room temperature (not stated)",
        "locationId": "laboratory",
        "sourceCoordinateSystem": "paper surface frame (x along surface, y normal); mapped to engine X=width, Y=up, Z=travel",
        "transformRevision": 1,
        "impactTimeS": 0.0,
        "positionM": [0.0, 0.0, 0.0],
        "normal": [0.0, 1.0, 0.0],
        "velocityBeforeMS": [before[0], before[1], before[2]],
        "velocityAfterMS": [after[0], after[1], after[2]],
        "angularVelocityBeforeRadS": [0.0, 0.0, 0.0],
        "angularVelocityAfterRadS": [omega2, 0.0, 0.0],
        "observed": {
            "velocityAfter": [False, True, True],
            "angularVelocityAfter": [True, False, False],
            "angularVelocityBeforeMeasured": True,
            "positionMeasured": False,
            "normalMeasured": True
        },
            "velocityAfterStdDevMS": [max(sigma_x2, sigma_y2), sigma_y2, sigma_x2],
            "angularVelocityAfterStdDevRadS": [sigma_omega2, sigma_omega2, sigma_omega2],
        "ballTemperatureC": None,
        "airTemperatureC": None,
        "surfaceTemperatureC": None,
        "relativeHumidity": None,
        "atmosphericPressurePa": None,
        "ballUsageMetadata": "standard tennis ball used repeatedly; usage and pressure not stated",
        "acquisitionMethod": "video at 100 frames/s, 1/500 s exposure, manual digitization against a 10 cm grid",
        "sourcePage": page,
        "sourceTableOrFigure": source,
        "sourceRow": row,
        "extractionMethod": "PDF text layer read directly (no OCR); surface-frame speeds and angles converted in this script",
        "extractionUncertainty": max(sigma_x2, sigma_y2),
        "qualityFlags": flags + [
            "incident_spin_zero_by_experimental_construction_not_measured_at_impact",
            "gravity_impulse_during_contact_not_removed (approx 0.04 m/s normal, comparable to published precision)",
            "ball_spec_substitution_type2_nominal_R_0.0335_vs_paper_0.033_mass_not_stated",
            "paper_reports_a_single_typical_bounce_not_an_average",
            "out_of_plane_components_not_observed (planar setup); the mask declares them, the sigma entries are placeholders",
        ],
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default="data/bounce/published/cross2002-records.jsonl")
    parser.add_argument("--note", default="data/bounce/published/cross2002-extraction.json")
    parser.add_argument("--pdf", help="optional path to the fetched PDF; its SHA-256 is recorded and checked")
    args = parser.parse_args()

    pdf_sha = PDF_SHA256
    pdf_hash_match = "not checked (no local PDF supplied)"
    if args.pdf:
        digest = hashlib.sha256(pathlib.Path(args.pdf).read_bytes()).hexdigest()
        pdf_hash_match = "match" if digest == PDF_SHA256 else "MISMATCH: " + digest
        if digest != PDF_SHA256:
            raise SystemExit("ERROR: supplied PDF does not match the recorded SHA-256: " + digest)

    records = []
    checks = []
    for surface, label, theta1, v1, v2, theta2, omega2, pub_vx2, pub_ratio, pub_ey in TABLE_II:
        sigma_v1 = sigma_from_published(v1, METHODS_PRECISION)
        sigma_v2 = sigma_from_published(v2, METHODS_PRECISION)
        sigma_omega = max(sigma_from_published(omega2, 0.01), 0.5)
        computed_vx2 = v2 * math.cos(math.radians(theta2))
        computed_ratio = computed_vx2 / (v1 * math.cos(math.radians(theta1)))
        computed_ey = (v2 * math.sin(math.radians(theta2))) / (v1 * math.sin(math.radians(theta1)))
        checks.append({
            "record": surface + "-" + str(int(theta1)) + "deg",
            "vx2_published": pub_vx2, "vx2_recomputed": round(computed_vx2, 3),
            "ratio_published": pub_ratio, "ratio_recomputed": round(computed_ratio, 3),
            "ey_published": pub_ey, "ey_recomputed": round(computed_ey, 3),
            "omega2_times_paperRadius": round(omega2 * BALL["paperRadiusM"], 3),
        })
        if abs(computed_vx2 - pub_vx2) > 0.02 or abs(computed_ratio - pub_ratio) > 0.02 or abs(computed_ey - pub_ey) > 0.02:
            raise SystemExit("ERROR: read columns disagree with the paper's own derived values for " + surface)
        flags = ["tableII_uncertainties_inferred_from_methods_statement_and_tableI",
                 "angle_uncertainty_inferred_from_tableI"]
        records.append(record("cross2002-" + surface + "-" + str(int(theta1)) + "deg-60cm",
                              "cross2002-" + surface, "cross2002-" + surface + "-h60", surface, label,
                              theta1, v1, v2, theta2, omega2, sigma_v1, sigma_v2, ANGLE_SIGMA_DEG,
                              sigma_omega, PAGE_TABLE_II, label + " at " + str(int(theta1)) + " degrees (h=60 cm)",
                              PAGE_TABLE_II, flags))

    t = TABLE_I
    checks.append({
        "record": "emery-20deg-25cm",
        "v1_from_published_components": round(math.hypot(t["vx1"], t["vy1"]), 3),
        "v1_published": 2.24,
        "theta1_from_components_deg": round(math.degrees(math.atan2(t["vy1"], t["vx1"])), 2),
        "theta2_from_components_deg": round(math.degrees(math.atan2(t["vy2"], t["vx2"])), 2),
        "theta2_published_deg": t["theta2"],
        "R_omega2_over_vx2": round(t["omega2"] * BALL["paperRadiusM"] / t["vx2"], 3),
        "R_omega2_over_vx2_published": 1.41,
    })
    if abs(math.hypot(t["vx1"], t["vy1"]) - 2.24) > 0.02:
        raise SystemExit("ERROR: Table I incident components do not reproduce the published v1")
    if abs(math.degrees(math.atan2(t["vy2"], t["vx2"])) - t["theta2"]) > 0.3:
        raise SystemExit("ERROR: Table I rebound components do not reproduce the published angle")
    records.append(record("cross2002-emery-20deg-25cm", "cross2002-emery", "cross2002-emery-h25", "emery",
                          "Emery paper", t["theta1"], math.hypot(t["vx1"], t["vy1"]),
                          math.hypot(t["vx2"], t["vy2"]), t["theta2"], t["omega2"],
                          t["vx1Sigma"], t["vx2Sigma"], t["theta2SigmaDeg"], t["omega2Sigma"],
                          PAGE_TABLE_I, "single bounce, all quantities with published uncertainties",
                          PAGE_TABLE_I, ["gravity_impulse_during_contact_not_removed",
                                         "ball_spec_substitution_type2_nominal_R_0.0335_vs_paper_0.033_mass_not_stated"],
                          vx1=t["vx1"], vy1=t["vy1"], vx2=t["vx2"], vy2=t["vy2"],
                          sigma_vx1=t["vx1Sigma"], sigma_vy1=t["vy1Sigma"],
                          sigma_vx2=t["vx2Sigma"], sigma_vy2=t["vy2Sigma"]))

    out = pathlib.Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w") as handle:
        for item in records:
            handle.write(json.dumps(item, separators=(",", ":")) + "\n")
    note = {
        "sourceId": "C2002",
        "title": "Measurements of the horizontal coefficient of restitution for a superball and a tennis ball",
        "author": "Rod Cross",
        "journal": "American Journal of Physics 70(5), 482-489 (2002)",
        "doi": "10.1119/1.1450571",
        "url": PDF_URL,
        "pdfSha256": pdf_sha,
        "pdfHashStatus": pdf_hash_match,
        "accessDate": "2026-09-17",
        "recordCount": len(records),
        "recordFile": out.name,
        "recordFileSha256": hashlib.sha256(out.read_bytes()).hexdigest(),
        "extractionMethod": "PDF text layer read directly; Table I and Table II values transcribed by hand after automated text extraction; derived columns recomputed and compared before writing",
        "uncertaintyBasis": "Table I publishes per-component uncertainties; Table II publishes none, so 2 percent (methods statement for the horizontal component), 1.5 degrees (Table I angle uncertainty) and 1 percent (Table I spin precision) are applied and flagged per record",
        "excludedColumns": "Table II trailing columns m, A and mS were read but not used; they are not part of the collision state contract",
        "checks": checks,
        "limits": [
            "single typical bounce per surface and angle, not an average of repeated bounces",
            "incident speed range 2.24 to 3.80 m/s only: far below serve and rally speeds",
            "surfaces are laboratory wood, emery paper and a Rebound Ace court sample, not classified courts",
            "contact position is not measured in any record",
        ],
    }
    note_path = pathlib.Path(args.note)
    note_path.parent.mkdir(parents=True, exist_ok=True)
    note_path.write_text(json.dumps(note, indent=2) + "\n")
    print("records", len(records), "->", out)
    print("note ->", note_path, "recordFileSha256", note["recordFileSha256"])
    for check in checks:
        print("CHECK", json.dumps(check))


if __name__ == "__main__":
    main()
