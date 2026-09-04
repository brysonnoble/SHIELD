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

# Detector confidence floor for candidate detections - not an engagement
# gate. See AVS.03 for the >=25% confidence-while-en-route requirement,
# enforced in the GCS UI (gcs_ui.ENGAGE_CONFIDENCE_FLOOR).
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

# AVS.05.01: the link to the video source shall be heartbeat-monitored.
# Every frame request doubles as that heartbeat - if Unity doesn't answer
# within this many seconds, the link is declared dead rather than left to
# whatever a bare socket recv() would eventually time out at.
UNITY_READ_TIMEOUT_SEC = 2.0

# --- Webcam fallback ---
WEBCAM_INDEX = 0
