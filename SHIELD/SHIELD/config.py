# Central configuration for the SHIELD detect/track pipeline.
# Values here are shared between emulation (PC) and, eventually, hardware
# (NVIDIA Jetson Orin Nano + a stereo pair of e-CAM25_CUONX) runs.

# --- Model ---
# Custom YOLO fine-tuned on a drone+balloon dataset (see "Custom
# drone/balloon model" section in the repo root README.md), replacing
# the stock COCO-pretrained weights. Trained on AI.Panther (the seed=1 run;
# val mAP50 0.939, mAP50-95 0.699, drone class 0.978 / 0.720); the Orin
# uses an INT8 engine built from this file by deploy/export_int8.py.
MODEL_PATH = "training/weights/drone_balloon_yolo26s_best.pt"
DEVICE = "cuda"

# Detector confidence floor for candidate detections - not an engagement
# gate. See AVS.03 for the >=25% confidence-while-en-route requirement,
# enforced in the GCS UI (gcs_ui.ENGAGE_CONFIDENCE_FLOOR).
CONFIDENCE_THRESHOLD = 0.25

# The custom model has two classes: "drone" and "balloon". Set to e.g.
# ["drone"] to ignore balloon detections (e.g. if balloons are only
# useful as a low-stakes tracking-practice target, not an engagement
# target) without retraining or touching the model itself.
CLASS_FILTER = None

# --- Stereo ranging ---
# Each target's range and relative position come from a stereo pair of
# e-CAM25_CUONX cameras (see stereo_ranging.py). The baseline and focal
# length arrive with every stereo frame (the Unity virtual camera sends
# its own; on hardware they'll come from the pair's calibration), so
# only matching settings live here.
# Ask the Unity virtual camera for stereo frames. Off = mono frames and
# no ranging, e.g. to compare against a build without the stereo rig.
STEREO_ENABLED = True

# Nearest range searched for. Sets the widest disparity searched
# (f * B / this), so lowering it costs matching time on every target.
STEREO_MIN_RANGE_M = 1.0

# Minimum normalized cross-correlation (-1..1) between the target's box
# in the left image and its best match in the right before a range is
# trusted. Below it the frame gives no fix and the last one is kept.
STEREO_MIN_MATCH_SCORE = 0.6

# Disparities below this many pixels are reported as no fix: range error
# grows as 1/d^2, so a sub-pixel shift is too far away to range usefully.
# 1 px is ~200 m with the default 20 cm baseline.
STEREO_MIN_DISPARITY_PX = 1.0

# Per-track exponential smoothing of position (0 = frozen, 1 = no
# smoothing).
STEREO_RANGE_SMOOTHING = 0.3

# ByteTrack, bundled with ultralytics.
TRACKER_CONFIG = "bytetrack.yaml"

# Duplicate-box suppression, applied between detection and tracking (see
# SHIELDDetector._non_duplicate_indices). A lower-confidence box of the
# same class is dropped as a duplicate when at least this fraction of the
# smaller box lies inside the other...
DUPLICATE_BOX_CONTAINMENT = 0.9
# ...and their centers are within this fraction of the larger box's
# width/height of each other. Observed duplicates sit within ~0.15; a
# second drone partly behind a nearer one is rarely this well centered,
# but if it is, it's suppressed until the two separate.
DUPLICATE_BOX_CENTER_OFFSET = 0.3

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
