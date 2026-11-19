"""The features the fall model sees.

This file and src/ElderSafe.Api/Detection/FallFeatures.cs must compute the
same values in the same order. The C# parity test (REQ-FALL-01) fails if they
drift apart, using the fixture written by train.py.
"""
import numpy as np

FEATURE_NAMES = [
    "mag_min",
    "mag_max",
    "mag_mean",
    "mag_std",
    "peak_minus_dip_s",
    "tail_mag_mean",
    "tail_mag_std",
    "orientation_change_deg",
]

HEAD_S = 1.0  # posture before the event
TAIL_S = 1.5  # stillness after the event
EPS = 1e-6    # boundary tolerance, so tick rounding in C# cannot move a sample across an edge


def compute_features(t, xyz):
    """t: seconds, shape (n,). xyz: acceleration in g, shape (n, 3), sorted by t."""
    t = np.asarray(t, dtype=np.float64) - t[0]
    xyz = np.asarray(xyz, dtype=np.float64)
    mag = np.sqrt((xyz ** 2).sum(axis=1))
    i_min, i_max = int(np.argmin(mag)), int(np.argmax(mag))
    head = t <= HEAD_S + EPS
    tail = t >= t[-1] - TAIL_S - EPS
    a, b = xyz[head].mean(axis=0), xyz[tail].mean(axis=0)
    cos = np.clip(a @ b / (np.linalg.norm(a) * np.linalg.norm(b) + 1e-9), -1.0, 1.0)
    return np.array(
        [
            mag.min(),
            mag.max(),
            mag.mean(),
            mag.std(),  # population standard deviation (ddof=0), as in C#
            t[i_max] - t[i_min],
            mag[tail].mean(),
            mag[tail].std(),
            np.degrees(np.arccos(cos)),
        ],
        dtype=np.float32,
    )
