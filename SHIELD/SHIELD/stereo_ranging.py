# Stereo ranging: locates each detected target in 3D from a rectified
# stereo pair - two identical cameras side by side, the reference (left)
# camera on the left, image rows aligned. Modeled on a pair of
# e-CAM25_CUONX (AR0234) cameras; the Unity virtual camera renders exactly
# such a pair (see CameraStreamer.cs).
#
# Only the target's own box is matched, not the whole frame: the box
# from the left image is searched for along the same rows of the right
# image, and how far it shifted (the disparity d, in pixels) gives depth
#     Z = f * B / d
# with f the focal length in pixels and B the baseline in meters. The
# box center then projects out to a relative position. Matching just the
# box keeps the cost well under a millisecond per target, and keeps the
# background (sky, ground at another depth) from deciding the answer the
# way it does for a full-frame depth map.

from dataclasses import dataclass

import cv2
import numpy as np

import config


@dataclass
class StereoFrame:
    """The right camera's image for one stereo capture, plus the
    calibration needed to range with it. The left image is the frame the
    detector already has."""

    right: np.ndarray  # BGR, same size as the left frame
    baseline_m: float  # distance between the two camera centers
    focal_px: float  # focal length in pixels at this image size (square pixels)


class StereoRanger:
    def __init__(
        self,
        min_range_m=config.STEREO_MIN_RANGE_M,
        min_match_score=config.STEREO_MIN_MATCH_SCORE,
        min_disparity_px=config.STEREO_MIN_DISPARITY_PX,
    ):
        self.min_range_m = min_range_m
        self.min_match_score = min_match_score
        self.min_disparity_px = min_disparity_px

    def locate(self, left, stereo, xyxy):
        """Returns one (right, up, forward) position in meters - relative
        to the left camera, forward along its line of sight - or None per
        box in xyxy. All None for a mono source (stereo is None)."""
        if stereo is None or len(xyxy) == 0:
            return [None] * len(xyxy)

        h, w = left.shape[:2]
        # Principal point at the image center: true of Unity's cameras, and
        # of a real pair once it's been calibrated and rectified.
        cx, cy = (w - 1) / 2.0, (h - 1) / 2.0
        # The nearest allowed range sets how far to search: a target at
        # min_range_m shifts this many pixels between the two images.
        max_disparity = int(np.ceil(stereo.focal_px * stereo.baseline_m / self.min_range_m))

        positions = []
        for box in xyxy:
            disparity = self._match_disparity(left, stereo.right, box, max_disparity)
            if disparity is None:
                positions.append(None)
                continue
            z = stereo.focal_px * stereo.baseline_m / disparity
            u, v = (box[0] + box[2]) / 2.0, (box[1] + box[3]) / 2.0
            x = (u - cx) * z / stereo.focal_px
            y_down = (v - cy) * z / stereo.focal_px
            positions.append((x, -y_down, z))
        return positions

    def _match_disparity(self, left, right, box, max_disparity):
        # Only the box and its search strip are converted to grayscale, not
        # the whole 1920x1200 frames.
        h, w = left.shape[:2]
        x1, y1 = max(0, int(np.floor(box[0]))), max(0, int(np.floor(box[1])))
        x2, y2 = min(w, int(np.ceil(box[2]))), min(h, int(np.ceil(box[3])))
        if x2 - x1 < 3 or y2 - y1 < 3:
            return None
        template = cv2.cvtColor(left[y1:y2, x1:x2], cv2.COLOR_BGR2GRAY)
        if template.std() < 1.0:
            return None  # featureless (e.g. a box of plain sky) - nothing to match on

        # In the right image the target sits d pixels further left, so the
        # template's left edge lands somewhere in [x1 - max_disparity, x1].
        sx1 = max(0, x1 - max_disparity)
        strip = cv2.cvtColor(right[y1:y2, sx1:x2], cv2.COLOR_BGR2GRAY)
        if strip.shape[1] < template.shape[1]:
            return None
        scores = cv2.matchTemplate(strip, template, cv2.TM_CCOEFF_NORMED)[0]
        best = int(np.argmax(scores))
        if not np.isfinite(scores[best]) or scores[best] < self.min_match_score:
            return None

        # Sub-pixel peak from a parabola through the best score and its
        # neighbors - at 20 m a whole pixel of disparity is ~10% of range.
        offset = 0.0
        if 0 < best < len(scores) - 1:
            a, b, c = scores[best - 1], scores[best], scores[best + 1]
            denom = a - 2 * b + c
            if denom < 0:
                offset = 0.5 * (a - c) / denom

        disparity = x1 - (sx1 + best + offset)
        if disparity < self.min_disparity_px:
            return None  # too far away to range (or a bad match on the wrong side)
        return disparity
