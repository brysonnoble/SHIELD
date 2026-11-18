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

# Nearest DEPTH (distance along the line of sight, not straight-line
# range) searched for. Sets the widest disparity searched (f * B / this:
# ~503 px at 0.4 m with the 20 cm baseline). A target off to the side is
# shallower than its range - at 1.0 m the first value here missed every
# off-center 1 m test spawn - so keep this below the nearest test range
# (AVS_Edge_Case_Test's 0.5 m). Not 0: the limit is what rules out
# physically impossible matches, e.g. a second identical drone further
# left in the same rows.
STEREO_MIN_RANGE_M = 0.4

# Only the central part of each box is matched, this fraction of its width
# and height. A quad's box is largely background seen between its arms,
# and that background barely shifts between the two images while the
# drone shifts by the full disparity - at close range (hundreds of px of
# disparity) it dragged the match score of the whole box under
# STEREO_MIN_MATCH_SCORE. The core is mostly the drone's own body.
STEREO_TEMPLATE_CORE = 0.5
# Smallest and largest real-world span (m) a target can have. A box s px
# across then can only be at a disparity between B*s/max and B*s/min, so
# each box searches just that band instead of the whole window above. This
# is what stops a wide window from returning confident, very wrong, very
# close ranges: in offline tests a 20 m drone matched stray ground texture
# at "0.9 m", and one of two identical drones in the same rows matched the
# other at "0.5 m". Generous on purpose - a drone seen edge-on or clipped by
# the frame looks smaller than its span.
STEREO_TARGET_SPAN_M = (0.1, 2.0)

# ...but never shrink the matched patch below this many pixels a side (or
# the box itself, if smaller), so small far-away boxes keep enough
# texture to match on.
STEREO_TEMPLATE_MIN_PX = 16

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

# Every frame request doubles as a liveness check on the video link - if
# Unity doesn't answer within this many seconds, the link is declared dead
# rather than left to whatever a bare socket recv() would eventually time
# out at. (The drone -> GCS heartbeat, AVS.05.01, is separate: see below.)
UNITY_READ_TIMEOUT_SEC = 2.0

# --- Drone <-> GCS command link (gcs_link.py) ---
# Engage/abort/Land Now/Shut Down commands in (AVS.05), heartbeat out
# (AVS.05.01). Localhost TCP in emulation; the telemetry radio on hardware.
# Unity's ports are 5555-5558 and the GNC SITL's is 5561.
GCS_LINK_HOST = "127.0.0.1"
GCS_LINK_PORT = 5560
HEARTBEAT_HZ = 1.0
# The GCS window flags the drone link as lost once this long passes with
# no heartbeat (three missed beats at 1 Hz).
HEARTBEAT_TIMEOUT_SEC = 3.0

# --- Webcam fallback ---
WEBCAM_INDEX = 0
