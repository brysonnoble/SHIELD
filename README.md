# SHIELD

**S.H.I.E.L.D.** — System for High Integrity Elimination of Low-Altitude
Drones — is a Florida Institute of Technology senior design (capstone)
project building an autonomous counter-UAS (C-UAS) system. SHIELD watches
a designated area, detects and tracks small low-altitude aerial targets
(drones, and balloons as a non-drone control case), and intercepts a
locked target under either full autonomy or human-approved engagement.

This repo is the software component, written and maintained by Bryson
Noble (Avionics Lead, Software Engineering) as part of the larger Team
S.H.I.E.L.D. capstone — a joint Aerospace Engineering (AEE) and Software
Engineering (SWE) team of 11 students, advised by Firat Irmak, PhD
(Aerospace Engineering Department, Florida Tech). See
[**Team & project links**](#team--project-links) below for the full
team roster and project site.

## Overview

The project spans two disciplines: AEE builds the airframe, propulsion,
and interception mechanism; SWE (this repo) builds the perception and
autonomy stack — detection, tracking, and the decision logic that hands
off between autonomous and manually-overridden engagement. The target
hardware is an **NVIDIA Jetson Orin Nano + e-CAM25_CUNOX (AR0234 global
shutter camera)** mounted on the drone, running the same Python pipeline
that this repo lets you build and test on a Windows PC first, using
Unity as a stand-in camera feed instead of the physical camera.

## Vision

Keep a designated area under constant watch, and count on SHIELD to
respond the moment an unauthorized drone comes within range. Once a
target is spotted, SHIELD locks on, follows its every move, and
intercepts it within 20 meters — no manual aiming or tracking required.
Operators decide exactly how much control they want: engage manual
override to personally direct the response, or let SHIELD's onboard AI
handle detection and engagement entirely on its own. Once locked on,
SHIELD stays locked on, tracking through brief obstructions or awkward
angles instead of losing the target and having to reacquire it from
scratch.

The novel piece is that **semi-autonomous** middle ground: SHIELD
detects a target and prompts an operator to engage, but once approved,
tracking and interception run on onboard edge computing rather than a
continuous ground-operator link — making the system harder to jam or
hack once it's pursuing a target.

## Project status

This is a two-semester capstone. First-semester milestones (Requirement
Document, Design Document, Test Plan, and three progress milestones
through detection/tracking, the custom drone/balloon model, and
emulation-level interception) run September–November 2026; a second
semester of hardware integration and final demo follows in spring 2027.
Only the `Emulation` platform mode (PC-based, Unity or webcam camera
feed) is implemented today — `Hardware` (Jetson Orin Nano + e-CAM25_CUNOX)
and `Prototype` modes are reserved for later milestones. See
[Known limitations](#known-limitations) below and the itemized milestone
tasks in the [project plan](#team--project-links).

## Architecture

```
Unity "SHIELD Virtual Camera"  --TCP/JPEG-->  Python pipeline
  (or a real webcam / video file)                  |
                                                   v
                                    OpenCV preprocessing
                                                   v
                                    Ultralytics YOLO (detection)
                                                   v
                                    ByteTrack (persistent IDs, via
                                    ultralytics' bundled tracker)
                                                   v
                                    Per-track OpenCV Kalman filter
                                    (smoothing + motion prediction)
                                                   v
                                    Target state (id, class, confidence,
                                    smoothed position) -> console / display
```

On the real drone this same Python pipeline runs on the Orin Nano, with
its onboard GPU accelerating YOLO inference and the e-CAM25_CUNOX as the
source. Locally, `--source unity` or `--source webcam` stand in for that
camera.

## Local emulation setup

The rest of this document covers getting the PC-based emulation running
after cloning the repo. It does not cover the STE test harness
(`Test Tools/STE`) or the mechanical/avionics side of the project.

## Prerequisites

- **Windows 11**
- **Python 3.11+** on PATH ([python.org/downloads](https://www.python.org/downloads/))
- **Unity Hub** + **Unity Editor 6000.3.11f1** (only if you want the
  Unity virtual camera; `--source webcam` works without Unity at all)
- Internet access on first run (YOLO model weights auto-download, ~5 MB)

Inference defaults to GPU (`config.py`, `DEVICE = "cuda"`) to match the
Orin Nano's onboard GPU at inference time. On a dev PC without an
NVIDIA GPU (or with a PyTorch build that doesn't match your driver),
switch this to `DEVICE = "cpu"`.

## Quick start

From the repo root:

```
setup.bat
```

This creates a virtual environment, installs all Python dependencies,
and pre-downloads the YOLO model. See [`setup.bat`](setup.bat) for what
it does step by step — it's a thin wrapper around the manual steps
below, safe to re-run.

Then, to run against your webcam immediately (no Unity needed):

```
cd "SHIELD\SHIELD"
.venv\Scripts\python __main__.py 0 --source webcam
```

Press `q` in the preview window to quit.

## Running against the Unity virtual camera

1. Open `Test Tools\SHIELD Virtual Camera` in Unity Hub (Editor
   6000.3.11f1).
2. Open `Assets/Scenes/SHIELD Virtual Camera.unity` — the only scene
   in the project. (Earlier versions of this project had a separate
   scene per lighting environment, selected by build index; those
   scenes have been removed in favor of the day/night skybox swap
   described below.) It already has a Main Camera with the **Camera
   Streamer** component (`Assets/Scripts/CameraStreamer.cs`) attached.
3. Enter Play Mode. The console should log
   `[CameraStreamer] Listening on 127.0.0.1:5555`.
4. In a terminal:

   ```
   cd "SHIELD\SHIELD"
   .venv\Scripts\python __main__.py 0 --source unity
   ```

   The Python process retries the connection for a few seconds if
   Unity isn't listening yet — start Play Mode first, or just re-run
   the command.

`Assets/STE Shared Data/VirtualEnvironment.txt` selects the skybox,
applied live (polled roughly once a second, no scene reload needed)
by the `SceneSelector` component in the scene
(`Assets/Scripts/SceneSelector.cs`):

| Value | Skybox |
|---|---|
| `0` | Day (`Assets/Misc/Materials/HDRI SkyLightBox [Day].mat`) |
| `1` | Night (`Assets/Misc/Materials/HDRI SkyLightBox [Dusk].mat`) |

`VirtualEnvironment.txt` is normally written by the STE test harness;
it currently defaults to `0` so Play Mode works standalone. Any value
other than `0` or `1` is logged as an error in the Unity console and
ignored — note the STE harness's own `VirtualEnvironment` enum
(`Common_Test_Variables.vb`) predates this change and may still emit
its old `0`-`6` range until that side is updated to match.

## Command-line reference

```
python __main__.py <platform> [--source {unity,webcam,file}] [--file PATH]
                    [--camera-index N] [--no-display]
```

- `platform` — `0` = Emulation (the only one implemented today). `1`
  (Hardware) and `2` (Prototype) are reserved for the Jetson Orin Nano
  build and will exit with a clear message if selected.
- `--source` — `unity` (default), `webcam`, or `file` (requires
  `--file`).
- `--camera-index` — webcam device index, default `0`.
- `--no-display` — skip the OpenCV preview window; detections still
  print to the console.

## Configuration

`SHIELD/SHIELD/config.py` holds the shared knobs:

| Setting | Purpose |
|---|---|
| `MODEL_PATH` | YOLO weights file. |
| `CONFIDENCE_THRESHOLD` | Minimum detection confidence (0.90, traceable to SRR requirement SYS.07). |
| `CLASS_FILTER` | Restrict detection to specific class names from the custom model, e.g. `["drone"]` to ignore balloon detections. `None` = keep both classes. |
| `TRACKER_CONFIG` | ByteTrack config bundled with ultralytics. |
| `TRACK_MAX_AGE` | Frames a track can go undetected before its Kalman filter is dropped. |
| `UNITY_HOST` / `UNITY_PORT` | Must match `CameraStreamer.cs`'s `port` field. |
| `WEBCAM_INDEX` | Default webcam device. |

## Custom drone/balloon model

`SHIELD/SHIELD/training/` fine-tunes YOLO11n on a combined drone +
balloon dataset in place of the stock COCO weights. Three raw Kaggle
downloads feed one combined training set — two drone sources (one
mostly close-range, one with more distance/scale variety, to help
detection of small/far-away targets) plus one balloon source:

- `prepare_combined_dataset.py` — reads both raw downloads (expected
  already unzipped under `datasets/`, which is gitignored — large
  binary files don't belong in a public repo), remaps the balloon
  set's labels from class 0 to class 1 so the two don't collide,
  prefixes filenames per source, and writes the merged result to
  `datasets/combined_yolo/{images,labels}/{train,val}/` plus
  `training/data.yaml`. Rerun this on each machine you train from.
  `data.yaml` itself has no machine-specific paths (Ultralytics
  resolves its `train:`/`val:` entries relative to the yaml file's own
  location when no `path:` key is set) so it's a small, portable,
  committed file.
- `prepare_dataset.py` — the original drone-only variant (splits just
  the drone dataset into `datasets/drone_yolo/`). Kept around for a
  single-class drone-only run; not used by `train.py` today.
- `train.py` — fine-tunes `yolo11n.pt` on `training/data.yaml`. Trains
  on GPU automatically if CUDA is available (see `Compute` note
  below), falls back to CPU otherwise. Raw run artifacts (checkpoints,
  plots, `last.pt`) go to `training/runs/`, gitignored along with all
  other `*.pt` files. The one exception: the final `best.pt` gets
  copied to `training/weights/drone_balloon_yolo11n_best.pt`, which IS
  committed - so a second machine gets the trained model via
  `git pull` rather than retraining or manually copying a file over.

**Datasets:**
- [Drone Dataset (UAV)](https://www.kaggle.com/datasets/dasmehdixtr/drone-dataset-uav),
  Mehdi Özel. 1,359 single-class (`drone`) images, YOLO-format
  annotations. License as listed on Kaggle: copyright of original
  authors.
- [Drone YOLO Detection](https://www.kaggle.com/datasets/sshikamaru/drone-yolo-detection),
  sshikamaru. ~4,010 labeled single-class (`drone`) images plus
  unlabeled background frames (kept as negatives), YOLO-format
  annotations, wider range of distances/scales than the dataset above.
  License: CC BY 4.0.
- [Balloon Object Detection](https://www.kaggle.com/datasets/serhiibiruk/balloon-object-detection),
  Serhii Biruk. 2,365 single-class (`balloon`) images, YOLO-format
  annotations (Roboflow export). License: MIT.

**Compute:** training device is independent of inference device — a
model trained on GPU runs identically on the Orin Nano's onboard GPU at
inference time, since only training speed is affected.

## Known limitations

- Only `platform 0` (Emulation) is implemented — `Hardware` (Jetson Orin
  Nano + e-CAM25_CUNOX) and `Prototype` modes don't exist yet.
- The Unity scene has no target object or motion scripting yet.
- `CameraStreamer.cs` reads pixels from the screen backbuffer during
  `WaitForEndOfFrame`, so the Game view needs to actually be rendering
  (Play Mode active) — it won't work in a fully headless batch build
  without further changes.

## Troubleshooting

- **`Could not connect to Unity virtual camera`** — Play Mode isn't
  running, or `CameraStreamer.cs` isn't attached to a camera, or the
  port doesn't match `config.py`'s `UNITY_PORT`.
- **`Could not open webcam index 0`** — another app has the camera
  open, or try `--camera-index 1`.
- **`ConnectionResetError [WinError 10054]` right after "Connected to
  Unity"** — the Python side now auto-reconnects instead of crashing,
  but the underlying cause is on the Unity side. Check the Unity
  Console for a `[CameraStreamer]` warning/error logged around the same
  time — common causes: a script recompile stopped Play Mode, the
  scene's camera/GameObject was destroyed, or a previous Play session
  is still holding port 5555 (stop it, or change `port` on the
  `CameraStreamer` component).
- **Slow inference / CUDA errors** — `DEVICE = "cuda"` requires an
  NVIDIA GPU and a matching PyTorch build; confirm your driver with
  `nvidia-smi` first, since an old driver can silently fall back to CPU
  or error out. Switch `DEVICE` to `"cpu"` in `config.py` if you don't
  have a GPU available; `yolo11n.pt` (the default) is the fastest model
  on CPU.

## Team & project links

- **Project site:** [brysonnoble.github.io/SHIELD](https://brysonnoble.github.io/SHIELD/)
- **First semester plan:** [SHIELD First Semester Plan.pdf](https://brysonnoble.github.io/SHIELD/Files/FA26/SHIELD%20First%20Semester%20Plan.pdf)
- **First semester plan presentation:** [SHIELD First Semester Plan Presentation.pptx](https://brysonnoble.github.io/SHIELD/Files/FA26/SHIELD%20First%20Semester%20Plan%20Presentation.pptx)

**Faculty advisor / client:** Firat Irmak, PhD — Aerospace Engineering
Department, Florida Institute of Technology

**Software maintainer:** Bryson Noble — Avionics Lead, Software Engineering

Team S.H.I.E.L.D. is an 11-person AEE/SWE capstone team; the full
roster, contact emails, and milestone deliverables are on the
[project site](https://brysonnoble.github.io/SHIELD/).
