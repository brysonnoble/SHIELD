# Detection + tracking pipeline: Ultralytics YOLO for detection, its
# bundled ByteTrack for persistent IDs, and a per-track OpenCV Kalman
# filter for smoothing/prediction, matching the architecture in the
# SHIELD Software Stack Summary.

from dataclasses import dataclass

import cv2
import numpy as np
from ultralytics import YOLO

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
        self.kf.statePost = np.array([[cx], [cy], [0], [0]], dtype=np.float32)
        self.age_since_seen = 0

    def predict(self):
        state = self.kf.predict()
        return float(state[0]), float(state[1])

    def correct(self, cx, cy):
        measurement = np.array([[cx], [cy]], dtype=np.float32)
        state = self.kf.correct(measurement)
        self.age_since_seen = 0
        return float(state[0]), float(state[1])


class SHIELDDetector:
    def __init__(
        self,
        model_path=config.MODEL_PATH,
        device=config.DEVICE,
        confidence_threshold=config.CONFIDENCE_THRESHOLD,
        class_filter=config.CLASS_FILTER,
        tracker_config=config.TRACKER_CONFIG,
        max_track_age=config.TRACK_MAX_AGE,
    ):
        self.model = YOLO(model_path)
        self.device = device
        self.confidence_threshold = confidence_threshold
        self.tracker_config = tracker_config
        self.max_track_age = max_track_age
        self._class_ids = self._resolve_class_ids(class_filter)
        self._trackers = {}  # track_id -> _CentroidKalmanFilter

    def _resolve_class_ids(self, class_filter):
        if not class_filter:
            return None
        name_to_id = {name: idx for idx, name in self.model.names.items()}
        return [name_to_id[name] for name in class_filter if name in name_to_id]

    def process_frame(self, frame):
        results = self.model.track(
            frame,
            persist=True,
            conf=self.confidence_threshold,
            classes=self._class_ids,
            tracker=self.tracker_config,
            device=self.device,
            verbose=False,
        )

        detections = []
        seen_ids = set()
        boxes = results[0].boxes
        if boxes is not None and boxes.id is not None:
            xyxy = boxes.xyxy.cpu().numpy()
            ids = boxes.id.cpu().numpy().astype(int)
            confs = boxes.conf.cpu().numpy()
            classes = boxes.cls.cpu().numpy().astype(int)

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
