"""Loaders for the public fall datasets used to train and test the fall model.

Every loader returns trials from the waist sensor only, in g, resampled to
20 Hz to match the dashboard simulator (ScenarioLibrary samples every 0.05 s).

Datasets (never committed; see the model card for licences and citations):
- UCI "Simulated Falls and Daily Living Activities" (Ozdemir & Barshan, 2014),
  CC BY 4.0. Waist sensor 340535, 25 Hz, acceleration in m/s^2.
- UMAFall (Casilari & Santoyo-Ramon, 2018), CC BY-NC 4.0. About 20.4 Hz,
  acceleration in g, five sensors interleaved in one CSV.
"""
import re
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from scipy.signal import resample_poly

TARGET_HZ = 20
WINDOW_S = 4.0  # = Alerting:DetectionWindow
WINDOW_N = int(WINDOW_S * TARGET_HZ)
G = 9.80665

UCI_WAIST_SENSOR = "340535"

# Falls where the person gets back up. ElderSafe defines a fall alert as
# "fell and stayed down" (RISK-01; the stillness stage in ThresholdFallDetector),
# so these are left out of both training and evaluation.
UCI_RECOVERY_FALLS = {"905", "906", "914", "916"}

# A gap longer than this inside a UMAFall trial means lost samples, not jitter.
MAX_GAP_S = 0.5


@dataclass
class Trial:
    dataset: str
    subject: str
    kind: str  # "fall" or "adl"
    activity: str
    xyz: np.ndarray  # (n, 3), in g, at TARGET_HZ


def load_uci(root):
    """Expects the waist files only: <root>/<subject>/Testler Export/<activity>/Test_<n>/340535.txt.
    Never extract the subjects' Bilgi.txt files: they hold volunteers' personal details."""
    trials = []
    for f in sorted(Path(root).rglob(f"{UCI_WAIST_SENSOR}.txt")):
        subject, activity = f.parts[-5], f.parts[-3][:3]
        if activity in UCI_RECOVERY_FALLS:
            continue
        rows = []
        for line in f.read_text(errors="replace").splitlines():
            if not line or line.startswith(("//", "Counter")):
                continue
            c = line.split("\t")
            try:
                rows.append((float(c[9]), float(c[10]), float(c[11])))
            except (ValueError, IndexError):
                continue
        if len(rows) < 25 * WINDOW_S:
            continue
        # 25 Hz -> 20 Hz with an anti-aliasing filter. padtype="line" stops the
        # zero padding from inventing a "free fall" at the trial edges.
        xyz = resample_poly(np.array(rows) / G, 4, 5, axis=0, padtype="line")
        kind = "fall" if activity.startswith("9") else "adl"
        trials.append(Trial("uci", subject, kind, activity, xyz))
    return trials


def load_uma(root):
    trials, skipped = [], 0
    for f in sorted(Path(root).glob("UMAFall_Subject_*.csv")):
        m = re.match(r"UMAFall_Subject_(\d+)_(ADL|Fall)_(.+?)_\d+_\d{4}-", f.name)
        if not m:
            continue
        subject, kind, activity = m.group(1), m.group(2).lower(), m.group(3)
        waist_id, ts, xyz = None, [], []
        for line in f.read_text(errors="replace").splitlines():
            s = line.strip()
            if s.startswith("%"):
                p = [x.strip() for x in s.lstrip("%").split(";")]
                if len(p) >= 3 and p[2] == "WAIST":
                    waist_id = p[1]
                continue
            p = s.split(";")
            # Sensor type 0 = accelerometer.
            if waist_id is not None and len(p) >= 7 and p[5] == "0" and p[6] == waist_id:
                ts.append(float(p[0]) / 1000.0)
                xyz.append((float(p[2]), float(p[3]), float(p[4])))
        if len(ts) < 2:
            skipped += 1  # this recording has no waist sensor
            continue
        t, idx = np.unique(np.array(ts), return_index=True)
        xyz = np.array(xyz)[idx]
        if np.diff(t).max() > MAX_GAP_S or t[-1] - t[0] < WINDOW_S:
            skipped += 1
            continue
        grid = np.arange(t[0], t[-1], 1.0 / TARGET_HZ)
        xyz = np.column_stack([np.interp(grid, t, xyz[:, k]) for k in range(3)])
        trials.append(Trial("uma", subject, "fall" if kind == "fall" else "adl", activity, xyz))
    return trials, skipped


def peak_index(xyz):
    return int(np.argmax(np.linalg.norm(xyz, axis=1)))


def fall_windows(xyz, rng, copies=5):
    """Windows around the impact. The inference window slides, so the peak is
    placed 1-2 s into the window at random, leaving 2-3 s of the aftermath."""
    peak, out = peak_index(xyz), []
    for _ in range(copies):
        start = peak - int(rng.uniform(1.0, 2.0) * TARGET_HZ)
        if start >= 0 and start + WINDOW_N <= len(xyz):
            out.append(xyz[start:start + WINDOW_N])
    return out


def pre_fall_windows(xyz, stride=TARGET_HZ):
    """Ordinary activity before the fall starts: windows ending 1 s before the impact."""
    end = peak_index(xyz) - TARGET_HZ
    return [xyz[s:s + WINDOW_N] for s in range(0, end - WINDOW_N + 1, stride)]


def adl_windows(xyz, stride=TARGET_HZ):
    return [xyz[s:s + WINDOW_N] for s in range(0, len(xyz) - WINDOW_N + 1, stride)]


def sliding_windows(xyz, stride=5):
    """What the API sees: a 4 s window re-evaluated as readings arrive (every 0.25 s here)."""
    return [xyz[s:s + WINDOW_N] for s in range(0, len(xyz) - WINDOW_N + 1, stride)]
