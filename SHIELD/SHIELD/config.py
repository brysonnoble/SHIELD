# Central configuration for the SHIELD detect/track pipeline.
# Values here are shared between emulation (PC) and, eventually, hardware
# (Raspberry Pi 5 + AI HAT+) runs.

# --- Model ---
# Custom YOLO11n fine-tuned on a drone-only dataset (see
# training/README section in the repo root README.md), replacing the
# stock COCO-pretrained weights.
MODEL_PATH = "training/weights/drone_yolo11n_best.pt"
DEVICE = "cpu"

# SYS.07: targeting system shall be >=90% confident before engaging.
CONFIDENCE_THRESHOLD = 0.90

# The custom model only has one class ("drone"), so no filtering is
# needed. Set to e.g. ["drone"] if more classes are added later and
# detection should stay restricted to a subset.
CLASS_FILTER = None

# ByteTrack, bundled with ultralytics.
TRACKER_CONFIG = "bytetrack.yaml"

# Drop a track's Kalman filter if it hasn't been matched to a detection
# for this many consecutive frames.
TRACK_MAX_AGE = 30

# --- Unity virtual camera stream ---
UNITY_HOST = "127.0.0.1"
UNITY_PORT = 5555
UNITY_CONNECT_RETRIES = 15
UNITY_RETRY_DELAY_SEC = 1.0

# --- Webcam fallback ---
WEBCAM_INDEX = 0
