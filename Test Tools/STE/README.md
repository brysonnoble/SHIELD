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
| `CameraController` (`Assets/Scripts/CameraController.cs`) | `5558` | `CAM RESET` | Resets the GameObject it's attached to (the Main Camera) back to world position `(0, 0, 0)`. |

These are driven from the STE test library's `EditVirtualEnvironment`,
`InstDrone`, and `TestCaseBegin`/`TestCaseEnd` functions
(`Common_Test_Functions.vb`); see `Test Scripts/AVS/Drone_Spawn_Test.vb` for
an example. `DroneSpawner` and `CameraController` are components not yet
wired into `SHIELD Virtual Camera.unity` — add `DroneSpawner` to a
GameObject and assign its three drone prefab fields, and add
`CameraController` to the Main Camera (alongside `CameraStreamer`), in the
Inspector before their commands will do anything.

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
4. For each checked test, in order: launches the Unity player and the
   Python emulation pipeline (`python __main__.py 0 --source unity`),
   waits for Unity's scene command listeners to come up, waits the
   **Settings** page's configurable startup delay (default 15s) for
   everything else — the Python preview window included — to finish
   opening, runs the script's test cases, then closes both programs.
5. Click **Stop** at any point to kill that whole tree (STE_Test_Solution,
   Unity, and Python together) before it finishes on its own.

Each run's log is written under the **Settings** page's configurable log
path (default `%LOCALAPPDATA%\SHIELD STE\Logs`), one subfolder per test
name and then one subfolder per run named by that run's start timestamp:
`<LogDirectory>\<TestName>\<yyyy-MM-dd_HH-mm-ss>\<TestName>.log`.

## Writing a test script

Add a `.vb` file under `Test Scripts\` (see
`Test Scripts\AVS\Drone_Spawn_Test.vb`), following `Example_Test.vb`'s
shape: a `Sub Main()` calling `BeginTest()`, one or more `TCxx()` test
cases, then `EndTest()`. Each `TCxx()` should call `TestCaseBegin()` first
and `TestCaseEnd()` last, and `TraceTo("REQ_NAME")` once per requirement it
tests. It's picked up automatically — no project file to edit — the next
time STE_Test_Solution is rebuilt and STE is launched.
Test-case library functions (`STE_Test_Solution\STE_Test_Solution\lib\Common_Test_Functions.vb`):

| Function | Effect |
|---|---|
| `BeginTest()` | Launches Unity + Python, waits for both to be ready, opens that run's log file. |
| `EndTest()` | Closes both programs and the log file. |
| `EditVirtualEnvironment(VirtualEnvironment.Day` / `.Night)` | Switches the skybox. |
| `InstDrone(DroneType.Quad` / `.Toad` / `.BumbleBee, x, y, z)` | Spawns a drone at the given world coordinates. |
| `TestCaseBegin()` | Call at the start of each `TCxx()`. Resets the Main Camera to `(0, 0, 0)` and logs the test case's start. |
| `TestCaseEnd()` | Call at the end of each `TCxx()`. Resets the camera again and logs the test case's end. |
| `TraceTo(requirementName As String)` | Call once per requirement a test case exercises, after `TestCaseBegin()`. Logs that requirement as covered by the current test case. |
