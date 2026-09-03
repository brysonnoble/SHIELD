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
after cloning the repo, plus the STE test harness
([Testing with the STE harness](#testing-with-the-ste-harness) below). It
does not cover the mechanical/avionics side of the project.

## Prerequisites

- **Windows 11**
- **Python 3.11+** on PATH ([python.org/downloads](https://www.python.org/downloads/))
- **Unity Hub** + **Unity Editor 6000.3.11f1** (only if you want the
  Unity virtual camera; `--source webcam` works without Unity at all)
- Internet access on first run (YOLO model weights auto-download, ~19 MB)

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

1. Open `Test Tools\SHIELD Virtual Camera\SHIELD Virtual Camera` in Unity
   Hub (Editor 6000.3.11f1) — the Editor project itself; the outer
   `Test Tools\SHIELD Virtual Camera\` folder is where its built standalone
   player lives (see [Testing with the STE harness](#testing-with-the-ste-harness)
   below), not the project.
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

The scene's skybox and drones are driven live by the STE test harness
over TCP rather than shared files, via two more listener components in
the scene (both bound to `127.0.0.1` like `CameraStreamer`, on their own
ports so they don't collide with the camera stream):

| Component | Port | Command | Effect |
|---|---|---|---|
| `SceneSelector` (`Assets/Scripts/SceneSelector.cs`) | `5556` | `ENV DAY` / `ENV NIGHT` | Swaps `RenderSettings.skybox` between `Assets/Misc/Materials/HDRI SkyLightBox [Day].mat` and `[Dusk].mat`, no scene reload. |
| `DroneSpawner` (`Assets/Scripts/DroneSpawner.cs`) | `5557` | `SPAWN <Quad\|Toad\|BumbleBee> <x> <y> <z>` | Instantiates the matching `Assets/Prefabs/_Drone [...]` prefab at the given world coordinates. |

Both are driven from the STE test library's `EditVirtualEnvironment` and
`InstDrone` functions (`Common_Test_Functions.vb`); see
`Test Scripts/AVS/Drone_Spawn_Test.vb` for an example. `DroneSpawner` is
a new component not yet wired into `SHIELD Virtual Camera.unity` — add it
to a GameObject in the scene and assign its three drone prefab fields in
the Inspector before its commands will do anything.

## Testing with the STE harness

`Test Tools\STE\` holds two projects that automate the manual Unity +
Python workflow above for the test plan:

- **STE** (`STE\STE.csproj`) — a WinUI3 desktop app: a checklist of test
  scripts plus Run/Stop/Settings.
- **STE_Test_Solution** (`STE_Test_Solution\STE_Test_Solution\STE_Test_Solution.vbproj`) —
  a VB.NET console app that compiles every script under this repo's
  `Test Scripts\` folder into one exe. STE launches it once per checked
  test, passing that test's name as a command-line argument; `Program.vb`
  dispatches to the matching script's `Sub Main`.

### One-time setup

1. Build a standalone Windows player for that scene (**File > Build
   Settings > Build**) to `Test Tools\SHIELD Virtual Camera\`, filename
   `SHIELD Virtual Camera.exe` — test scripts launch this player directly,
   so the Editor doesn't need to be open or in Play Mode to run a test.
2. Build both `Test Tools\STE\STE_Test_Solution\STE_Test_Solution.sln` and
   `Test Tools\STE\STE.sln` (Visual Studio, or `dotnet build`).

### Running a test

1. Launch the built `STE.exe`.
2. Check one or more scripts in the list (scanned from `Test Scripts\` at
   startup; `Example_Test.vb` is excluded as a template, not a real test)
   and click **Run**.
3. For each checked test, in order: launches the Unity player and the
   Python emulation pipeline (`python __main__.py 0 --source unity`),
   waits for Unity's scene command listeners to come up, waits the
   **Settings** page's configurable startup delay (default 15s) for
   everything else — the Python preview window included — to finish
   opening, runs the script's test cases, then closes both programs.
4. Click **Stop** at any point to kill that whole tree (STE_Test_Solution,
   Unity, and Python together) before it finishes on its own.

### Writing a test script

Add a `.vb` file under `Test Scripts\` (see
`Test Scripts\AVS\Drone_Spawn_Test.vb`), following `Example_Test.vb`'s
shape: a `Sub Main()` calling `BeginTest()`, one or more `TCxx()` test
cases, then `EndTest()`. It's picked up automatically — no project file to
edit — the next time STE_Test_Solution is rebuilt and STE is launched.
Test-case library functions (`STE_Test_Solution\STE_Test_Solution\lib\Common_Test_Functions.vb`):

| Function | Effect |
|---|---|
| `BeginTest()` | Launches Unity + Python, waits for both to be ready. |
| `EndTest()` | Closes both. |
| `EditVirtualEnvironment(VirtualEnvironment.Day` / `.Night)` | Switches the skybox. |
| `InstDrone(DroneType.Quad` / `.Toad` / `.BumbleBee, x, y, z)` | Spawns a drone at the given world coordinates. |

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

`SHIELD/SHIELD/training/` fine-tunes YOLO26s on a combined drone +
balloon dataset in place of the stock COCO weights. YOLO26 (Ultralytics,
released January 2026) is a from-scratch redesign for edge/low-power
deployment with NMS-free end-to-end inference, a good fit for the Orin
Nano target. Three raw Kaggle
downloads plus one Unity-rendered synthetic set feed one combined
training set — three drone sources (two real-photo, one synthetic) plus
one balloon source:

- `prepare_combined_dataset.py` — reads the raw downloads and the
  synthetic set (expected already unzipped/generated under `datasets/`,
  which is gitignored — large binary files don't belong in a public
  repo), remaps the balloon set's labels from class 0 to class 1 so the
  drone and balloon classes don't collide, prefixes filenames per
  source, and writes the merged result to
  `datasets/combined_yolo/{images,labels}/{train,val}/` plus
  `training/data.yaml`. Rerun this on each machine you train from.
  `data.yaml` itself has no machine-specific paths (Ultralytics
  resolves its `train:`/`val:` entries relative to the yaml file's own
  location when no `path:` key is set) so it's a small, portable,
  committed file.
- `prepare_dataset.py` — the original drone-only variant (splits just
  the drone dataset into `datasets/drone_yolo/`). Kept around for a
  single-class drone-only run; not used by `train.py` today.
- `train.py` — fine-tunes `yolo26s.pt` on `training/data.yaml`. Trains
  on GPU automatically if CUDA is available (see `Compute` note
  below), falls back to CPU otherwise. Raw run artifacts (checkpoints,
  plots, `last.pt`) go to `training/runs/`, gitignored along with all
  other `*.pt` files. The one exception: the final `best.pt` gets
  copied to `training/weights/drone_balloon_yolo26s_best.pt`, which IS
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
- Unity synthetic drone renders (`datasets/unity_drone_dataset/`).
  2,000 single-class (`drone`) 1920x1080 JPEGs with auto-generated
  YOLO-format labels, produced by the `DroneDatasetCollectorWindow`
  Unity Editor tool (`Test Tools/SHIELD Virtual Camera/Assets/Editor/
  SHIELD Virtual Camera Data Collector/DroneDatasetCollectorWindow.cs`)
  against the same virtual camera scene used for emulation. Randomizes
  drone placement/count (1-3 per frame), camera rotation, and day/night
  skybox; ground-truth boxes are computed from known scene transforms
  rather than hand-labeled. Fills the sim-domain gap (day/night, camera
  roll, multi-drone frames) the real-photo sources lack.

**Compute:** training device is independent of inference device — a
model trained on GPU runs identically on the Orin Nano's onboard GPU at
inference time, since only training speed is affected.

## Known limitations

- Only `platform 0` (Emulation) is implemented — `Hardware` (Jetson Orin
  Nano + e-CAM25_CUNOX) and `Prototype` modes don't exist yet.
- `DroneSpawner` can instantiate a drone on command, but nothing moves
  it afterward — there's no motion scripting yet, and the component
  itself still needs to be added to a GameObject in the scene (see
  above) before it can receive commands at all.
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
  have a GPU available; YOLO26 is designed for real-time CPU-only
  inference, though the smaller `yolo26n.pt` will run faster than the
  `yolo26s.pt` base this project trains on if CPU speed becomes a
  bottleneck.

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
