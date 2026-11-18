"""Train the ElderSafe fall model on UCI, test it once on UMAFall, export ONNX.

Evaluation is per trial, the way the product behaves: the 4 s window slides
over each recording, and a trial counts as "alerted" if any window scores at
or above the threshold. The threshold is chosen on UCI by subject-wise
cross-validation only. UMAFall is never used for any choice.

Outputs in --out:
  model.onnx                the model
  model.json                metadata the API reads (threshold, features, hash, metrics)
  fall_model_fixture.json   golden windows for the C# parity test (UCI data, CC BY 4.0)
"""
import argparse
import hashlib
import json
from pathlib import Path

import lightgbm as lgb
import mlflow
import numpy as np
import onnxruntime as ort
from onnxmltools import convert_lightgbm
from onnxmltools.convert.common.data_types import FloatTensorType
from sklearn.model_selection import GroupKFold

from datasets import (TARGET_HZ, WINDOW_N, WINDOW_S, adl_windows, fall_windows,
                      load_uci, load_uma, pre_fall_windows, sliding_windows)
from features import FEATURE_NAMES, compute_features
from rule_baseline import rule_fires

T = np.arange(WINDOW_N) / TARGET_HZ


def features(windows):
    return np.array([compute_features(T, w) for w in windows], dtype=np.float32).reshape(-1, len(FEATURE_NAMES))


def training_set(trials, rng):
    X, y = [], []
    for tr in trials:
        if tr.kind == "fall":
            pos, neg = fall_windows(tr.xyz, rng), pre_fall_windows(tr.xyz)
        else:
            pos, neg = [], adl_windows(tr.xyz)
        X += [features(pos), features(neg)]
        y += [np.ones(len(pos)), np.zeros(len(neg))]
    return np.vstack(X), np.concatenate(y)


def make_model():
    return lgb.LGBMClassifier(n_estimators=200, num_leaves=15, learning_rate=0.05,
                              min_child_samples=20, class_weight="balanced", verbose=-1)


def trial_scores(model, sliding):
    """Highest window score in each trial."""
    return np.array([model.predict_proba(s)[:, 1].max() if len(s) else 0.0 for s in sliding])


def report(prefix, trials, alerted):
    falls = np.array([t.kind == "fall" for t in trials])
    adl_hours = sum(len(t.xyz) for t in trials if t.kind == "adl") / TARGET_HZ / 3600
    return {
        f"{prefix}_fall_trials": int(falls.sum()),
        f"{prefix}_adl_trials": int((~falls).sum()),
        f"{prefix}_sensitivity": float(alerted[falls].mean()),
        f"{prefix}_adl_trials_alerted": float(alerted[~falls].mean()),
        f"{prefix}_false_alarms_per_adl_hour": float(alerted[~falls].sum() / adl_hours),
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--uci", required=True, help="folder holding the UCI waist files")
    p.add_argument("--uma", required=True, help="folder holding the UMAFall CSV files")
    p.add_argument("--out", required=True)
    p.add_argument("--target-sensitivity", type=float, default=0.95)
    p.add_argument("--data-version", default="local")
    a = p.parse_args()
    rng = np.random.default_rng(42)

    uci = load_uci(a.uci)
    uma, uma_skipped = load_uma(a.uma)
    print(f"UCI: {len(uci)} trials from {len({t.subject for t in uci})} subjects")
    print(f"UMAFall: {len(uma)} trials from {len({t.subject for t in uma})} subjects ({uma_skipped} without usable waist data)")

    uci_sliding = [features(sliding_windows(t.xyz)) for t in uci]
    uma_sliding = [features(sliding_windows(t.xyz)) for t in uma]

    # Subject-wise cross-validation on UCI. A subject is never in both the
    # training and the scoring side of a fold.
    subjects = np.array([t.subject for t in uci])
    oof = np.zeros(len(uci))
    for fold, (tr_idx, te_idx) in enumerate(GroupKFold(n_splits=5).split(uci, groups=subjects)):
        X, y = training_set([uci[i] for i in tr_idx], rng)
        model = make_model().fit(X, y)
        oof[te_idx] = trial_scores(model, [uci_sliding[i] for i in te_idx])
        print(f"fold {fold + 1}: held-out subjects {sorted(set(subjects[te_idx]))}")

    # Lowest threshold change that keeps the target sensitivity on unseen subjects.
    falls = np.sort(oof[[t.kind == "fall" for t in uci]])
    threshold = float(falls[int(np.floor((1 - a.target_sensitivity) * len(falls)))])

    metrics = {"threshold": threshold}
    metrics |= report("cv_model", uci, oof >= threshold)
    metrics |= report("cv_rule", uci, np.array([rule_fires(t.xyz, TARGET_HZ) for t in uci]))

    # Final model on all of UCI, then the one-off external test on UMAFall.
    X, y = training_set(uci, rng)
    model = make_model().fit(X, y)
    metrics |= report("external_model", uma, trial_scores(model, uma_sliding) >= threshold)
    metrics |= report("external_rule", uma, np.array([rule_fires(t.xyz, TARGET_HZ) for t in uma]))

    # Export to ONNX and prove it scores exactly like LightGBM.
    onx = convert_lightgbm(model, initial_types=[("features", FloatTensorType([None, len(FEATURE_NAMES)]))],
                           zipmap=False)
    raw = onx.SerializeToString()
    sess = ort.InferenceSession(raw, providers=["CPUExecutionProvider"])
    prob_name = next(o.name for o in sess.get_outputs() if "prob" in o.name)
    check = np.vstack(uma_sliding[:50])
    onnx_p = sess.run([prob_name], {"features": check})[0][:, 1]
    assert np.allclose(onnx_p, model.predict_proba(check)[:, 1], atol=1e-5), "ONNX export does not match LightGBM"

    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    (out / "model.onnx").write_bytes(raw)
    sha = hashlib.sha256(raw).hexdigest()
    (out / "model.json").write_text(json.dumps({
        "name": "eldersafe-fall",
        "threshold": threshold,
        "feature_names": FEATURE_NAMES,
        "sample_rate_hz": TARGET_HZ,
        "window_seconds": WINDOW_S,
        "input_name": "features",
        "output_name": prob_name,
        "sha256": sha,
        "data_version": a.data_version,
        "metrics": metrics,
    }, indent=2))

    # Golden windows for the C# parity test, from UCI (CC BY 4.0) only.
    picks = [t for t in uci if t.kind == "fall"][:3] + [t for t in uci if t.kind == "adl"][:3]
    fixture = []
    for t in picks:
        w = fall_windows(t.xyz, rng, copies=1)[0] if t.kind == "fall" else adl_windows(t.xyz)[0]
        f = compute_features(T, w)
        fixture.append({
            "samples": [[float(ti), *map(float, s)] for ti, s in zip(T, w)],
            "features": f.tolist(),
            "probability": float(sess.run([prob_name], {"features": f.reshape(1, -1)})[0][0, 1]),
        })
    (out / "fall_model_fixture.json").write_text(json.dumps({"source": "UCI Simulated Falls (CC BY 4.0)",
                                                             "windows": fixture}))

    print(json.dumps(metrics, indent=2))

    # Locally this goes to MLflow's default store; inside an Azure ML job the
    # tracking URI is set for you and the metrics appear in Studio.
    with mlflow.start_run():
        mlflow.log_params({"data_version": a.data_version, "window_s": WINDOW_S, "hz": TARGET_HZ,
                           "target_sensitivity": a.target_sensitivity, "sha256": sha})
        mlflow.log_metrics(metrics)


if __name__ == "__main__":
    main()
