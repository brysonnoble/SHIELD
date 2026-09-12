# STE test harness

STE (System Test Environment) automates the manual "Unity virtual camera +
Python pipeline" workflow described in the [root README](../../README.md)
for running the SHIELD test plan. This document covers STE itself; see the
root README for what SHIELD is and how to set up the emulation pipeline it
drives. Paths below are relative to the repo root unless noted otherwise.

## Projects

`Test Tools\STE\` holds two projects:

- **STE** (`STE\STE.csproj`) — a WinUI3 desktop app: a checklist of test
  scripts plus Run/Stop/Settings.
- **STE_Test_Solution** (`STE_Test_Solution\STE_Test_Solution\STE_Test_Solution.vbproj`) —
  a VB.NET console app that compiles every script under this repo's
  `Test Scripts\` folder into one exe. STE launches it once per checked
  test, passing that test's name as a command-line argument; `Program.vb`
  dispatches to the matching script's `Sub Main`.

## Unity-side listeners

Once Unity is in Play Mode (see the root README's "Running against the
Unity virtual camera"), the scene's skybox, drones, and camera position are
driven live by the STE test library over TCP rather than shared files, via
listener components in the scene (all bound to `127.0.0.1` like
`CameraStreamer`, each on its own port so they don't collide with the
camera stream):

| Component | Port | Command | Effect |
|---|---|---|---|
| `SceneSelector` (`Assets/Scripts/SceneSelector.cs`) | `5556` | `ENV DAY` / `ENV NIGHT` | Swaps `RenderSettings.skybox` between `Assets/Misc/Materials/HDRI SkyLightBox [Day].mat` and `[Dusk].mat`, no scene reload. |
| `DroneSpawner` (`Assets/Scripts/DroneSpawner.cs`) | `5557` | `SPAWN <Quad\|Toad\|BumbleBee> <x> <y> <z>` | Instantiates the matching `Assets/Prefabs/_Drone [...]` prefab at the given world coordinates. |
| `DroneSpawner` (`Assets/Scripts/DroneSpawner.cs`) | `5557` | `DESPAWN ALL` | Destroys every drone that `DroneSpawner` has instantiated so far. |
| `CameraController` (`Assets/Scripts/CameraController.cs`) | `5558` | `CAM RESET` | Resets the GameObject it's attached to (the Main Camera) back to world position `(0, 0, 0)`. |
| `CameraController` (`Assets/Scripts/CameraController.cs`) | `5558` | `QUIT` | Calls `Application.Quit()`, running Unity's own shutdown path instead of being closed/killed from the outside. |

`SceneSelector` and `DroneSpawner`'s `SPAWN` command are driven from the STE
test library's `EditVirtualEnvironment` and `InstDrone` functions, and
`DroneSpawner`'s `DESPAWN ALL` from `DespawnAllDrones()`
(`Common_Test_Functions.vb`); see `Test Scripts/AVS/AVS_Detection_Test.vb`
for an example. None of these three are invoked automatically by the test
library anymore now that `TestCaseBegin()` closes and relaunches Unity
fresh for every test case (see `STE_Test_Solution\README.md`) rather than
resetting/despawning between test cases in one long-lived Unity session -
a test case only needs to call `DespawnAllDrones()` itself if it wants its
drones gone before the case ends. `CAM RESET` has no library wrapper at
all yet. `QUIT`, on the other
hand, *is* used automatically, every time the test library closes Unity
(`CloseUnityPlayer()` in `Common_Test_Functions.vb`) - and it's the *only*
way Unity ever gets closed: an OS-level close/kill against a process with a
live graphics device open has crashed the GPU driver (BSOD) on this
project's hardware, so if Unity doesn't exit within 10s of being asked to
`QUIT`, it's left running (and that test case is marked ABORTed) rather
than the test library ever force-killing it.
`DroneSpawner` and `CameraController` need to be added to the scene once —
`DroneSpawner` on a GameObject with its three drone prefab fields assigned,
and `CameraController` on the Main Camera (alongside `CameraStreamer`) —
before their commands do anything.

## One-time setup

1. Build a standalone Windows player for that scene (**File > Build
   Settings > Build**) to `Test Tools\SHIELD Virtual Camera\`, filename
   `SHIELD Virtual Camera.exe` — test scripts launch this player directly,
   so the Editor doesn't need to be open or in Play Mode to run a test.
2. Build both `Test Tools\STE\STE_Test_Solution\STE_Test_Solution.sln` and
   `Test Tools\STE\STE.sln` (Visual Studio, or `dotnet build`).

## Running a test

1. Launch the built `STE.exe`.
2. Check one or more scripts in the list (scanned from `Test Scripts\` at
   startup; `Example_Test.vb` is excluded as a template, not a real test)
   and click **Run**.
3. **Run** first rebuilds `STE_Test_Solution.exe` (`dotnet build`, same as
   the "Build Test Solution" entry in **Settings**' program list, but
   unconditional - it runs regardless of whether that entry is checked)
   so it always reflects the latest edits to any test script, then aborts
   with no run if the build fails. No manual rebuild step needed after
   editing or adding a test script.
4. For each checked test, in order, `STE_Test_Solution.exe` runs that
   script's test cases one at a time - and for *each* test case: closes any
   already-running instance of the Unity player (an orphan left over from a
   previous crashed/killed run would otherwise hold onto its ports),
   launches a fresh one plus the Python emulation pipeline
   (`python __main__.py 0 --source unity`), waits for Unity's scene command
   listeners to come up, then waits up to 30s for Python's own confirmation
   that its video connection to Unity is actually up — aborting just that
   test case if it never happens — before waiting the **Settings** page's
   configurable startup delay (default 15s) for everything else — the
   Python preview window included — to finish opening, runs the test
   case, then closes both programs again before moving to the next one.
   Every test case therefore starts from the same empty scene, at the cost
   of paying the startup delay once per test case rather than once per
   script.
5. Click **Stop** at any point to kill that whole tree (STE_Test_Solution,
   Unity, and Python together) before it finishes on its own.

Each run's logs are written under the **Settings** page's configurable log
path (default `%LOCALAPPDATA%\SHIELD STE\Logs`), one subfolder per test
name and then one subfolder per run named by that run's start timestamp:
`<LogDirectory>\<TestName>\<yyyy-MM-dd_HH-mm-ss>\` - see
[`STE_Test_Solution\README.md`](STE_Test_Solution/README.md#log-files) for
what's in there.

## Writing a test script

See
[`STE_Test_Solution\README.md`](STE_Test_Solution/README.md) for how to add
a test script, the PASS/FAIL/ABORT test case result model, the log file
format, and the full test-case/assertion library reference
(`Common_Test_Functions.vb`/`Common_Test_Checks.vb`).
