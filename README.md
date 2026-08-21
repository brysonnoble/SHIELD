# SHIELD — Local Emulation Setup

S.H.I.E.L.D. (System for High Integrity Elimination of Low-Altitude Drones)
is a Florida Tech capstone C-UAS project: an autonomous UAS that detects,
tracks, and intercepts small aerial targets. The target hardware is a
**Raspberry Pi 5 + AI HAT+ (Hailo) + Camera Module 3**, but this repo lets
you build and test the detect/track pipeline on a Windows PC first, using
Unity as a stand-in camera feed.

This document covers getting that local emulation running after cloning
the repo. It does not cover the STE test harness (`Test Tools/STE`) or
the mechanical/avionics side of the project.

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

On the real drone this same Python pipeline runs on the Pi 5, with the
AI HAT+ accelerating YOLO inference and the Camera Module 3 as the
source. Locally, `--source unity` or `--source webcam` stand in for that
camera.

## Prerequisites

- **Windows 11**
- **Python 3.11+** on PATH ([python.org/downloads](https://www.python.org/downloads/))
- **Unity Hub** + **Unity Editor 6000.3.11f1** (only if you want the
  Unity virtual camera; `--source webcam` works without Unity at all)
- Internet access on first run (YOLO model weights auto-download, ~5 MB)

A discrete GPU is optional. Inference defaults to CPU (`config.py`,
`DEVICE = "cpu"`) to match the Pi 5's CPU-only fallback behavior and
avoid fighting mismatched CUDA driver versions.

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
cd "SHIELD\SHIELD\SHIELD"
.venv\Scripts\python __main__.py 0 --source webcam
```

Press `q` in the preview window to quit.

## Running against the Unity virtual camera

1. Open `Test Tools\SHIELD Virtual Camera` in Unity Hub (Editor
   6000.3.11f1).
2. Open a scene under `Assets/Scenes` (see the table below for the
   full list). Each one already has a Main Camera with the **Camera
   Streamer** component (`Assets/Scripts/CameraStreamer.cs`) attached.
3. Enter Play Mode. The console should log
   `[CameraStreamer] Listening on 127.0.0.1:5555`.
4. In a terminal:

   ```
   cd "SHIELD\SHIELD\SHIELD"
   .venv\Scripts\python __main__.py 0 --source unity
   ```

   The Python process retries the connection for a few seconds if
   Unity isn't listening yet — start Play Mode first, or just re-run
   the command.

`Assets/STE Shared Data/VirtualEnvironment.txt` selects which scene
loads on Play, matching the build index order in *File > Build
Settings* (also mirrored by the `VirtualEnvironment` enum in the STE
harness, `Common_Test_Variables.vb`):

| Index | Scene |
|---|---|
| `0` | `SHIELD Virtual Camera.unity` (initial scene) |
| `1` | `Cold Night/Cold Night.unity` |
| `2` | `Cold Sunset/Cold Sunset.unity` |
| `3` | `Deep Dusk/Deep Dusk.unity` |
| `4` | `Epic_BlueSunset/Epic_BlueSunset.unity` |
| `5` | `Night MoonBurst/Night Moon Burst.unity` |
| `6` | `Overcast Low/AllSky_Overcast4_Low.unity` |

`VirtualEnvironment.txt` is normally written by the STE test harness;
it currently defaults to `0` so Play Mode works standalone.

**Note:** scenes `1`–`6` are lighting/skybox environments (an HDRI
skybox, a matching material, and a Directional Light, plus the camera
from step 2 above). None of the scenes contain a moving target object,
and the detector is stock COCO-pretrained YOLO — it won't recognize a
scene with nothing in it. Point the camera at something, or add a
simple target GameObject to a scene, to see detections.

## Command-line reference

```
python __main__.py <platform> [--source {unity,webcam,file}] [--file PATH]
                    [--camera-index N] [--no-display]
```

- `platform` — `0` = Emulation (the only one implemented today). `1`
  (Hardware) and `2` (Prototype) are reserved for the Raspberry Pi
  build and will exit with a clear message if selected.
- `--source` — `unity` (default), `webcam`, or `file` (requires
  `--file`).
- `--camera-index` — webcam device index, default `0`.
- `--no-display` — skip the OpenCV preview window; detections still
  print to the console.

## Configuration

`SHIELD/SHIELD/SHIELD/config.py` holds the shared knobs:

| Setting | Purpose |
|---|---|
| `MODEL_PATH` | YOLO weights file. Swap to a custom-trained model once one exists (see below). |
| `CONFIDENCE_THRESHOLD` | Minimum detection confidence (0.90, traceable to SRR requirement SYS.07). |
| `CLASS_FILTER` | Restrict detection to specific COCO class names, e.g. `["sports ball", "kite", "bird"]`. `None` = all 80 classes. |
| `TRACKER_CONFIG` | ByteTrack config bundled with ultralytics. |
| `TRACK_MAX_AGE` | Frames a track can go undetected before its Kalman filter is dropped. |
| `UNITY_HOST` / `UNITY_PORT` | Must match `CameraStreamer.cs`'s `port` field. |
| `WEBCAM_INDEX` | Default webcam device. |

## Known limitations

- Only `platform 0` (Emulation) is implemented — `Hardware` (Pi 5 + AI
  HAT+) and `Prototype` modes don't exist yet.
- Detection uses a stock COCO-pretrained model. The stack plan calls for
  training a custom drone/balloon model via CVAT/Roboflow; once that
  exists, point `MODEL_PATH` at it and this pipeline needs no other
  changes.
- The Unity scenes have no target object or motion scripting yet.
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
- **Slow inference** — expected on CPU with larger models; `yolo11n.pt`
  (the default) is the fastest. Don't switch `DEVICE` to `"cuda"`
  without first confirming your installed PyTorch build supports your
  GPU driver (`nvidia-smi`) — an old driver can silently fall back to
  CPU or error out.
