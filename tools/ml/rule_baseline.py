"""Python port of ThresholdFallDetector, used only as a comparison baseline.

Evaluated per trial: does the rule fire in any 4 s window of the recording?
A free fall, then an impact within 1 s, then 2 s of stillness after a 0.5 s
settle, all fit inside one 4 s window, so checking every free-fall sample
across the trial is equivalent to sliding the window.

Values mirror appsettings.json "FallDetection". If they change there, change
them here.
"""
import numpy as np

FREE_FALL_G = 0.6
IMPACT_G = 2.5
MAX_FREE_FALL_TO_IMPACT_S = 1.0
SETTLE_S = 0.5
STILLNESS_S = 2.0
STILLNESS_TOLERANCE_G = 0.25


def rule_fires(xyz, hz):
    mag = np.linalg.norm(xyz, axis=1)
    t = np.arange(len(mag)) / hz
    for i in np.flatnonzero(mag <= FREE_FALL_G):
        deadline = t[i] + MAX_FREE_FALL_TO_IMPACT_S
        after = np.flatnonzero((t > t[i]) & (t <= deadline) & (mag >= IMPACT_G))
        if after.size == 0:
            continue
        start = t[after[0]] + SETTLE_S
        end = start + STILLNESS_S
        if t[-1] < end:
            continue
        still = mag[(t >= start) & (t <= end)]
        if still.size > 0 and np.all(np.abs(still - 1.0) <= STILLNESS_TOLERANCE_G):
            return True
    return False
