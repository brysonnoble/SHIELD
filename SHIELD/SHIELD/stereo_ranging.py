# Stereo ranging: locates each detected target in 3D from a rectified
# stereo pair - two identical cameras side by side, the reference (left)
# camera on the left, image rows aligned. Modeled on the Waveshare
# IMX219-83 (two IMX219s, 60 mm apart); the Unity virtual camera renders
# exactly such a pair (see CameraStreamer.cs).
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


@dataclass
class StereoFix:
    """One box's ranging result for one frame."""

    # (right, up, forward) meters from the left camera, or None if this
    # frame gave no fix.
    position_m: object
    # Best left/right match correlation found (-1..1), whether or not it
    # cleared min_match_score - None when no match was attempted (mono
    # source, box too small or featureless). Printed as "match=" so a
    # missing range can be diagnosed from the test logs.
    match_score: object


class StereoRanger:
    def __init__(
        self,
        min_range_m=config.STEREO_MIN_RANGE_M,
        min_match_score=config.STEREO_MIN_MATCH_SCORE,
        min_disparity_px=config.STEREO_MIN_DISPARITY_PX,
        template_core=config.STEREO_TEMPLATE_CORE,
        template_min_px=config.STEREO_TEMPLATE_MIN_PX,
        target_span_m=config.STEREO_TARGET_SPAN_M,
    ):
        self.min_span_m, self.max_span_m = target_span_m
        self.min_range_m = min_range_m
        self.min_match_score = min_match_score
        self.min_disparity_px = min_disparity_px
        self.template_core = template_core
        self.template_min_px = template_min_px

    def locate(self, left, stereo, xyxy):
        """Returns one StereoFix per box in xyxy: a (right, up, forward)
        position in meters - relative to the left camera, forward along its
        line of sight - or None, plus the match score. All empty for a mono
        source (stereo is None)."""
        if stereo is None or len(xyxy) == 0:
            return [StereoFix(None, None) for _ in xyxy]

        h, w = left.shape[:2]
        # Principal point at the image center: true of Unity's cameras, and
        # of a real pair once it's been calibrated and rectified.
        cx, cy = (w - 1) / 2.0, (h - 1) / 2.0
        # The nearest allowed range sets how far to search: a target at
        # min_range_m shifts this many pixels between the two images.
        max_disparity = int(np.ceil(stereo.focal_px * stereo.baseline_m / self.min_range_m))

        fixes = []
        for box in xyxy:
            # A target spanning min..max meters that looks `size` px
            # across is at depth f*span/size, i.e. disparity B*size/span
            # (f cancels) - only search that band. See STEREO_TARGET_SPAN_M.
            size = max(box[2] - box[0], box[3] - box[1])
            band = (
                stereo.baseline_m * size / self.max_span_m,
                min(max_disparity, stereo.baseline_m * size / self.min_span_m),
            )
            disparity, score = self._match_disparity(left, stereo.right, box, band)
            if disparity is None:
                fixes.append(StereoFix(None, score))
                continue
            z = stereo.focal_px * stereo.baseline_m / disparity
            u, v = (box[0] + box[2]) / 2.0, (box[1] + box[3]) / 2.0
            x = (u - cx) * z / stereo.focal_px
            y_down = (v - cy) * z / stereo.focal_px
            fixes.append(StereoFix((x, -y_down, z), score))
        return fixes

    def _core(self, lo, hi):
        """The centered template_core fraction of [lo, hi), widened to at
        least template_min_px (but never past the box itself)."""
        size = hi - lo
        core = min(size, max(self.template_min_px, int(round(size * self.template_core))))
        start = lo + (size - core) // 2
        return start, start + core

    def _match_disparity(self, left, right, box, band):
        """Searches disparities in band = (lo, hi) px. Returns (disparity in
        px or None, best match score or None)."""
        min_disparity = max(0, int(np.floor(band[0])))
        max_disparity = int(np.ceil(band[1]))
        if max_disparity <= min_disparity:
            return None, None
        # Only the box and its search strip are converted to grayscale, not
        # the whole frames.
        h, w = left.shape[:2]
        x1, y1 = max(0, int(np.floor(box[0]))), max(0, int(np.floor(box[1])))
        x2, y2 = min(w, int(np.ceil(box[2]))), min(h, int(np.ceil(box[3])))
        if x2 - x1 < 3 or y2 - y1 < 3:
            return None, None

        # Match both the core of the box (config.STEREO_TEMPLATE_CORE -
        # less of the background seen between a quad's arms) and the whole
        # box, and keep whichever matches better. Neither wins on every
        # airframe: the core fixed close Quads and Toads, but the BumbleBee
        # at 1 m only matched as a whole box. The disparity is the same for
        # any part of the target, so this changes what's matched, not the
        # math - and it's two small matches, still well under a millisecond.
        regions = [(*self._core(x1, x2), *self._core(y1, y2))]
        if regions[0] != (x1, x2, y1, y2):
            regions.append((x1, x2, y1, y2))
        results = [self._match_region(left, right, r, min_disparity, max_disparity) for r in regions]
        ranged = [r for r in results if r[0] is not None]
        if ranged:
            return max(ranged, key=lambda r: r[1])
        scores = [r[1] for r in results if r[1] is not None]
        return None, (max(scores) if scores else None)

    def _match_region(self, left, right, region, min_disparity, max_disparity):
        """Matches left[y1:y2, x1:x2] along the same rows of the right image.
        Returns (disparity in px or None, best match score or None)."""
        x1, x2, y1, y2 = region
        template = cv2.cvtColor(left[y1:y2, x1:x2], cv2.COLOR_BGR2GRAY)
        if template.std() < 1.0:
            return None, None  # featureless (e.g. a box of plain sky) - nothing to match on

        # In the right image the target sits d pixels further left, so the
        # template's left edge lands somewhere in
        # [x1 - max_disparity, x1 - min_disparity].
        sx1 = max(0, x1 - max_disparity)
        sx2 = x2 - min_disparity
        if sx2 <= sx1:
            return None, None
        strip = cv2.cvtColor(right[y1:y2, sx1:sx2], cv2.COLOR_BGR2GRAY)
        if strip.shape[1] < template.shape[1]:
            return None, None
        scores = cv2.matchTemplate(strip, template, cv2.TM_CCOEFF_NORMED)[0]
        best = int(np.argmax(scores))
        score = float(scores[best])
        if not np.isfinite(score):
            return None, None
        if score < self.min_match_score:
            return None, score

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
            return None, score  # too far away to range (or a bad match on the wrong side)
        return disparity, score
