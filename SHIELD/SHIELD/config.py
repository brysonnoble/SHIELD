# Central configuration for the SHIELD detect/track pipeline.
# Values here are shared between emulation (PC) and, eventually, hardware
# (Raspberry Pi 5 + AI HAT+) runs.

# --- Model ---
# ultralytics auto-downloads this on first run (needs internet once).
# "yolo11n.pt" is the smallest/fastest model, good for CPU-only emulation.
MODEL_PATH = "yolo11n.pt"
DEVICE = "cpu"

# SYS.07: targeting system shall be >=90% confident before engaging.
CONFIDENCE_THRESHOLD = 0.90

# Restrict to specific COCO class names, e.g. ["sports ball", "kite", "bird"].
# Empty/None = detect all 80 COCO classes (useful until a custom
# drone/balloon model is trained via CVAT/Roboflow per the stack plan).
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
