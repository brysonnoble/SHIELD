# Detection + tracking pipeline: Ultralytics YOLO for detection, its
# bundled ByteTrack for persistent IDs, and a per-track OpenCV Kalman
# filter for smoothing/prediction, matching the architecture in the
# SHIELD Software Stack Summary.

from dataclasses import dataclass

import cv2
import numpy as np
from ultralytics import YOLO
from ultralytics.trackers.byte_tracker import BYTETracker
from ultralytics.utils import YAML, IterableSimpleNamespace
from ultralytics.utils.checks import check_yaml

import config


@dataclass
class Detection:
    track_id: int
    class_name: str
    confidence: float
    bbox: tuple  # raw (x1, y1, x2, y2) from the detector
    smoothed_center: tuple  # Kalman-filtered (cx, cy)
    smoothed_bbox: tuple  # bbox re-centered on the smoothed center


class _CentroidKalmanFilter:
    """Tracks a single target's (cx, cy) with a constant-velocity model,
    smoothing detector noise and predicting position when a detection is
    momentarily missed.
    """

    def __init__(self, cx, cy):
        self.kf = cv2.KalmanFilter(4, 2)
        self.kf.transitionMatrix = np.array(
            [[1, 0, 1, 0], [0, 1, 0, 1], [0, 0, 1, 0], [0, 0, 0, 1]], dtype=np.float32
        )
        self.kf.measurementMatrix = np.array(
            [[1, 0, 0, 0], [0, 1, 0, 0]], dtype=np.float32
        )
        self.kf.processNoiseCov = np.eye(4, dtype=np.float32) * 1e-2
        self.kf.measurementNoiseCov = np.eye(2, dtype=np.float32) * 1e-1
        # Start at the first detection with an unknown velocity. The first
        # correct() runs without a predict() before it, so it reads the
        # *pre* state/covariance - OpenCV leaves those at zero, which made
        # every new track report (0, 0) and then slide in from the corner
        # over ~10 frames. Position is as certain as one measurement;
        # velocity is unknown, so it adapts within a frame or two.
        initial_state = np.array([[cx], [cy], [0], [0]], dtype=np.float32)
        initial_cov = np.diag([1e-1, 1e-1, 1e2, 1e2]).astype(np.float32)
        self.kf.statePre = initial_state.copy()
        self.kf.statePost = initial_state.copy()
        self.kf.errorCovPre = initial_cov.copy()
        self.kf.errorCovPost = initial_cov.copy()
        self.age_since_seen = 0

    def predict(self):
        state = self.kf.predict()
        return float(state[0, 0]), float(state[1, 0])

    def correct(self, cx, cy):
        measurement = np.array([[cx], [cy]], dtype=np.float32)
        state = self.kf.correct(measurement)
        self.age_since_seen = 0
        return float(state[0, 0]), float(state[1, 0])


class SHIELDDetector:
    def __init__(
        self,
        model_path=config.MODEL_PATH,
        device=config.DEVICE,
        confidence_threshold=config.CONFIDENCE_THRESHOLD,
        class_filter=config.CLASS_FILTER,
        tracker_config=config.TRACKER_CONFIG,
        max_track_age=config.TRACK_MAX_AGE,
        duplicate_containment=config.DUPLICATE_BOX_CONTAINMENT,
        duplicate_center_offset=config.DUPLICATE_BOX_CENTER_OFFSET,
    ):
        self.model = YOLO(model_path)
        self.device = device
        self.confidence_threshold = confidence_threshold
        self.max_track_age = max_track_age
        self.duplicate_containment = duplicate_containment
        self.duplicate_center_offset = duplicate_center_offset
        self._class_ids = self._resolve_class_ids(class_filter)
        self._trackers = {}  # track_id -> _CentroidKalmanFilter
        # ByteTrack is run here rather than through model.track(), so that
        # duplicate boxes can be removed between detection and tracking -
        # model.track() hands every box straight to the tracker, which then
        # gives each duplicate its own track ID.
        tracker_args = IterableSimpleNamespace(**YAML.load(check_yaml(tracker_config)))
        self._byte_tracker = BYTETracker(args=tracker_args)

    def _resolve_class_ids(self, class_filter):
        if not class_filter:
            return None
        name_to_id = {name: idx for idx, name in self.model.names.items()}
        return [name_to_id[name] for name in class_filter if name in name_to_id]

    def process_frame(self, frame):
        results = self.model.predict(
            frame,
            conf=self.confidence_threshold,
            classes=self._class_ids,
            device=self.device,
            verbose=False,
        )
        boxes = results[0].boxes.cpu().numpy()
        boxes = boxes[self._non_duplicate_indices(boxes.xyxy, boxes.conf, boxes.cls)]
        # Rows of (x1, y1, x2, y2, track_id, score, cls, detection_index)
        # for every track matched to a detection this frame.
        tracks = self._byte_tracker.update(boxes, frame)

        detections = []
        seen_ids = set()
        if len(tracks):
            xyxy = tracks[:, :4]
            ids = tracks[:, 4].astype(int)
            confs = tracks[:, 5]
            classes = tracks[:, 6].astype(int)

            for box, track_id, conf, cls_id in zip(xyxy, ids, confs, classes):
                x1, y1, x2, y2 = box
                cx, cy = (x1 + x2) / 2.0, (y1 + y2) / 2.0
                w, h = x2 - x1, y2 - y1

                tracker = self._trackers.get(track_id)
                if tracker is None:
                    tracker = _CentroidKalmanFilter(cx, cy)
                    self._trackers[track_id] = tracker
                else:
                    tracker.predict()
                scx, scy = tracker.correct(cx, cy)

                detections.append(
                    Detection(
                        track_id=int(track_id),
                        class_name=self.model.names[int(cls_id)],
                        confidence=float(conf),
                        bbox=(float(x1), float(y1), float(x2), float(y2)),
                        smoothed_center=(scx, scy),
                        smoothed_bbox=(scx - w / 2, scy - h / 2, scx + w / 2, scy + h / 2),
                    )
                )
                seen_ids.add(track_id)

        self._age_out_missed_tracks(seen_ids)
        return detections

    def _non_duplicate_indices(self, xyxy, confs, classes):
        """Indices of the boxes to keep after dropping duplicates of the
        same target: a box of the same class nested around (or inside) a
        higher-confidence box, sharing roughly its center.

        The detector tends to put one or two looser, lower-confidence boxes
        around a drone - most often one cut off by the frame edge or at
        mid range. Each fully contains the real box, but they overlap it
        too little (IoU as low as ~0.2) for the model's own NMS (IoU 0.7)
        to merge them, and ByteTrack would otherwise give every one of them
        its own track ID.
        """
        keep = []
        for i in np.argsort(-confs):
            if not any(
                classes[i] == classes[k] and self._is_duplicate(xyxy[i], xyxy[k]) for k in keep
            ):
                keep.append(i)
        return np.array(sorted(keep), dtype=int)

    def _is_duplicate(self, a, b):
        ix = max(0.0, min(a[2], b[2]) - max(a[0], b[0]))
        iy = max(0.0, min(a[3], b[3]) - max(a[1], b[1]))
        area_a = (a[2] - a[0]) * (a[3] - a[1])
        area_b = (b[2] - b[0]) * (b[3] - b[1])
        smaller_area = min(area_a, area_b)
        if smaller_area <= 0 or ix * iy / smaller_area < self.duplicate_containment:
            return False
        # Nested, but is it centered on the same thing? Measured against
        # the larger box, so a small drone that merely sits inside a near
        # drone's box (off to one side) isn't mistaken for a duplicate.
        larger = a if area_a >= area_b else b
        width, height = larger[2] - larger[0], larger[3] - larger[1]
        dx = abs((a[0] + a[2]) - (b[0] + b[2])) / 2
        dy = abs((a[1] + a[3]) - (b[1] + b[3])) / 2
        return dx <= self.duplicate_center_offset * width and dy <= self.duplicate_center_offset * height

    def _age_out_missed_tracks(self, seen_ids):
        stale = []
        for track_id, tracker in self._trackers.items():
            if track_id in seen_ids:
                continue
            tracker.age_since_seen += 1
            if tracker.age_since_seen > self.max_track_age:
                stale.append(track_id)
        for track_id in stale:
            del self._trackers[track_id]

    @staticmethod
    def annotate(frame, detections):
        annotated = frame.copy()
        for det in detections:
            x1, y1, x2, y2 = (int(v) for v in det.bbox)
            cv2.rectangle(annotated, (x1, y1), (x2, y2), (0, 255, 0), 2)

            scx, scy = det.smoothed_center
            cv2.circle(annotated, (int(scx), int(scy)), 4, (0, 0, 255), -1)

            label = f"ID {det.track_id} {det.class_name} {det.confidence:.2f}"
            cv2.putText(
                annotated, label, (x1, max(0, y1 - 8)),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1, cv2.LINE_AA,
            )
        return annotated
