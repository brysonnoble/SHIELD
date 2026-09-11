# STE_Test_Solution

STE_Test_Solution is the VB.NET console app the [STE app](../README.md)
launches to actually run a test script — see that document for what STE is
and how a test run works end to end. This document covers writing test
scripts and the test-case/assertion library they're built on. Paths below
are relative to the repo root unless noted otherwise.

## Writing a test script

Add a `.vb` file under `Test Scripts\<SUBSYSTEM>\` (`SYS`, `AER`, `AVS`,
`GNC`, or `STR` — matching the sheets in the team's requirements traceability
matrix), following `Test Scripts\Example_Test.vb`'s shape: a `Sub Main()`
calling `BeginTest()`, then `RunTestCase(AddressOf TCxx)` once per test
case, then `EndTest()`. `RunTestCase()` wraps each `TCxx()` with
`TestCaseBegin()` / `TestCaseEnd()` and records its result for
`EndTest()`'s summary as one of:

- **PASS** — the test case ran to completion and every assertion held.
- **FAIL** — the test case ran to completion, but an assertion
  (`Common_Test_Checks.Fail()`) determined the system under test didn't
  meet the requirement.
- **ABORT** — the test case hit an unhandled exception instead of a
  deliberate assertion failure (a bug, a broken scene/network setup, a
  missing Unity component, etc.) — nothing was actually verified either
  way, which is why it's tracked separately from FAIL.

Either way, one bad test case doesn't stop the rest of the script's test
cases (or `EndTest()`) from running. `TCxx()` itself just needs to call
`TraceTo("REQ_NAME")` once per requirement it exercises, then set up
whatever it needs and assert - each `TCxx()` should be independently
runnable (spawn its own drone, etc.) rather than depending on state a
previous test case in the same script left behind: `TestCaseBegin()` closes
and relaunches a fresh Unity + Python for every test case (see
"Per-test-case reload" below), so there's nothing left over from a previous
test case to depend on in the first place. A single `TCxx()`, or a
single script's spawned drone, can legitimately trace more than one
requirement (see `Test Scripts\AVS\AVS_Detection_Test.vb`) rather than
needing a one-script-per-requirement layout. For a requirement this harness
can't verify at all (a physical/hardware property, or a requirement whose
software doesn't exist yet), still add a `TCxx()` that calls `TraceTo()`
with no assertion, plus a `TODO` comment on what would verify it once
possible — see `Test Scripts\AER\AER_Requirements_Test.vb` for the pattern
(it'll show as a PASS in `EndTest()`'s summary, same as any other test case
with nothing that failed). New scripts are picked up automatically — no
project file to edit — the next time STE_Test_Solution is rebuilt and STE
is launched.

## Per-test-case reload

`TestCaseBegin()` closes any running instance of the Unity player and
Python pipeline, launches fresh ones, and waits for them to be ready -
*every* test case, not just once per script. That means:

- Every test case starts from the exact same empty scene (no leftover
  drones, no leftover camera position) without the test library needing to
  explicitly reset or despawn anything between test cases.
- Every test case pays its own copy of Unity/Python startup time plus the
  **Settings** page's configurable startup delay (default 15s) - a script
  with several test cases takes noticeably longer to run than one big
  BeginTest()-launches-once-for-the-whole-script model would, in exchange
  for each test case being fully independent and reproducible on its own.
- A check like `AssertDetectLatencyBelow` that reads the pipeline's
  `--profile` output only ever sees samples from its own test case's fresh
  process - a cold-start/model-warm-up spike from a previous test case's
  run can't leak into this one.
- `TestCaseBegin()` throws if the relaunched Unity's video connection to
  Python isn't confirmed within 30s, so `RunTestCase()` marks just that one
  test case ABORTed (per the PASS/FAIL/ABORT model above) rather than the
  whole script failing to run at all.
- Unity gets closed a lot more often than before (once per test case
  instead of once per script), so `TestCaseEnd()` closes it *only* via a
  `QUIT` scene command (`CameraController.cs`'s `Application.Quit()`) -
  never an OS-level close/kill, since `TerminateProcess` against a process
  with a live graphics device open has crashed the GPU driver (BSOD) on
  this project's hardware, even through a "try `CloseMainWindow()` first"
  fallback. If Unity doesn't exit within 10s of being asked to `QUIT`,
  `CloseUnityPlayer()` (`Common_Test_Functions.vb`) leaves it running
  rather than escalating to a kill, and marks that test case ABORTed
  instead - close the leftover window manually before the next run.

## Log files

Each run writes two log files under the STE app's **Settings** page's log
path (`<LogDirectory>\<TestName>\<yyyy-MM-dd_HH-mm-ss>\`):

- `<TestName>.log` - the readable one. Starts with the test name, start
  timestamp, machine name, and platform/source; then each test case's
  `TestCaseBegin`/`Trace`/`Output Check <n>`/`TestCaseEnd` lines as they
  happen; then `EndTest()`'s closing summary - an overall PASS/FAIL/ABORT
  (worst-outcome-wins: any ABORT beats a plain FAIL, which beats PASS), a
  per-status count, and every test case's result again (with which
  requirements it traced). This is the file to read after a run.
- `<TestName>_python_output.log` - the noisy one: every raw line the Python
  pipeline printed (a `target id=... class=... conf=...` line per detection
  per frame, a `[SHIELD][profile] ...` line every 30 frames, etc.), each
  timestamped. Only worth opening to debug a specific ABORT or an
  unexpected check result - it's not meant to be read top to bottom.

## Test-case library reference

Test-case library functions (`STE_Test_Solution\lib\Common_Test_Functions.vb`):

| Function | Effect |
|---|---|
| `BeginTest()` | Opens that run's two log files and writes the main one's header (test name, start time, machine, platform/source, both log paths). Doesn't touch Unity/Python itself - see `TestCaseBegin()`. |
| `EndTest()` | Closes the pipeline if it's somehow still running (a safety net - `TestCaseEnd()` already closes it after every test case), writes the overall PASS/FAIL/ABORT summary and each test case's result, and closes both log files. |
| `RunTestCase(testCase As Action)` | Call once per test case from `Main()`, e.g. `RunTestCase(AddressOf TC01)`, instead of calling `TCxx()` directly. Wraps it with `TestCaseBegin()`/`TestCaseEnd()`, catches a deliberate assertion failure (FAIL) or any other unhandled exception - including `TestCaseBegin()`'s own relaunch timing out (ABORT) - and either way lets the rest of the script's test cases (and `EndTest()`) still run. |
| `EditVirtualEnvironment(VirtualEnvironment.Day` / `.Night)` | Switches the skybox. |
| `InstDrone(DroneType.Quad` / `.Toad` / `.BumbleBee, x, y, z)` | Spawns a drone at the given world coordinates. |
| `TestCaseBegin()` / `TestCaseEnd()` | Called by `RunTestCase()` around each `TCxx()` - see "Per-test-case reload" above. `TestCaseBegin()` closes any already-running Unity player instance, launches a fresh Unity + Python (with `-u --profile`, so its output streams promptly and includes per-stage timing), waits for Unity's scene-command listeners, then up to 30s for Python's own confirmation that its video connection to Unity is up (throwing if that never happens), then the configurable startup delay. `TestCaseEnd()` closes both programs and logs the test case's PASS/FAIL/ABORT result and traced requirements. Only call these directly if you have a reason not to go through `RunTestCase()`. |
| `TraceTo(requirementName As String)` | Call once per requirement a test case exercises, anywhere in `TCxx()`. Logs that requirement as covered by the current test case and includes it in that test case's summary line. |

Pass/fail check functions (`STE_Test_Solution\lib\Common_Test_Checks.vb`),
for asserting against what the Python pipeline actually printed during the
current test (captured live since `BeginTest()` redirects its output - the
raw lines land in `_python_output.log`, not the main log):

| Function | Effect |
|---|---|
| `Pass()` | Logs `Output Check <n>: PASS`, `<n>` being a 1-based counter of checks made in the current test case (reset by `TestCaseBegin()`). |
| `Fail(expectedValue, actualValue)` | Logs `Output Check <n>: **FAIL** Expected Value: <expectedValue>, Actual Value: <actualValue>`, marks the test case FAILed, and throws `TestAssertionFailedException` so the rest of the calling `TCxx()` doesn't keep running. |
| `WaitForLogMatch(pattern, timeoutSeconds)` | Polls the pipeline's captured output for the FIRST regex match, returning it (or `Nothing` on timeout). The building block most checks below are written on top of. |
| `WaitForLatestLogMatch(pattern, timeoutSeconds)` | Like `WaitForLogMatch`, but returns the MOST RECENT match instead of the first - for a rolling reading (like `--profile`'s timing line) where an early sample shouldn't outweigh a later, steadier one. |
| `AssertLogMatches(pattern, timeoutSeconds)` | `Fail`s unless `pattern` appears within `timeoutSeconds`. |
| `AssertTargetDetected(className, timeoutSeconds)` | `Fail`s unless the detector reports a target of `className` within `timeoutSeconds` (AVS-01/AVS-02/SYS-03 style checks). |
| `MinConfidenceOverWindow(className, durationSeconds)` | Returns the lowest confidence seen for `className` over the next `durationSeconds` (or `Nothing` if it was never detected). |
| `AssertMinConfidenceAtLeast(className, threshold, durationSeconds)` | `Fail`s unless every reading in that window is at least `threshold` (AVS-03). |
| `AssertDetectLatencyBelow(maxMs, timeoutSeconds)` | `Fail`s unless the most recently reported `--profile` average detect time is under `maxMs` (AVS-04). |

These checks only see what the Python process prints to stdout/stderr — to
check something new, print a parseable line for it (matching the existing
`target id=... class=... conf=...` and `[SHIELD][profile] ... detect=Xms`
lines' style) rather than adding a new capture mechanism.
