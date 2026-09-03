# Central configuration for the SHIELD detect/track pipeline.
# Values here are shared between emulation (PC) and, eventually, hardware
# (NVIDIA Jetson Orin Nano + e-CAM25_CUNOX) runs.

# --- Model ---
# Custom YOLO fine-tuned on a drone+balloon dataset (see "Custom
# drone/balloon model" section in the repo root README.md), replacing
# the stock COCO-pretrained weights.
# TODO: switch back to a yolo26s build (drone_balloon_yolo26s_best.pt) once
# retrained - drone_balloon_yolo11n_best.pt is a stand-in until then.
MODEL_PATH = "training/weights/drone_balloon_yolo11n_best.pt"
DEVICE = "cuda"

# SYS.07: targeting system shall be >=90% confident before engaging.
CONFIDENCE_THRESHOLD = 0.05

# The custom model has two classes: "drone" and "balloon". Set to e.g.
# ["drone"] to ignore balloon detections (e.g. if balloons are only
# useful as a low-stakes tracking-practice target, not an engagement
# target) without retraining or touching the model itself.
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
