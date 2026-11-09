# GNC SITL

A C++ software-in-the-loop port of the GNC team's flight controller from
[SHIELD-S_GNC_PIPELINE](https://github.com/avijay2024/SHIELD-S_GNC_PIPELINE)
(pinned as the `External/SHIELD-S_GNC_PIPELINE` submodule). It runs their
discrete LQR against the 16-state quadrotor model it was designed on, and
reads their exported gains and vehicle parameters straight from the
submodule. Nothing in this folder keeps its own copy of their numbers.

| Source | Ported from `simulation/matlab_simulink/runShieldSimulation.m` |
|---|---|
| `src/vehicle_model.*` | `rigidDerivative`, `rotation`, first-order rotor response (as in `verifyEquations`), RK4 |
| `src/lqr_controller.*` | `u = uh - K (x - x_ref)`, `K` from `gain_<case>.csv` (as in `referenceSimulation`) |
| `src/guidance.hpp` | **Placeholder** - not theirs; see below |
| `src/io.*`, `src/main.cpp` | Windows command server, run loop, self-test and validation modes |

`vehicle_model` and `lqr_controller` have no platform-specific code, so
they can move into the GNC repo's planned portable `core/` unchanged.

It is not wired into the STE harness or the Unity scene, and the
detection pipeline doesn't talk to it.

## Build

```
"Test Tools\GNC SITL\build.bat"
```

Produces `build\shield_gnc_sitl.exe`. Uses MSVC when Visual Studio's C++
workload and a Windows 10/11 SDK are installed, otherwise g++ on PATH
(MSYS2 MinGW-w64). The submodule must be checked out:
`git submodule update --init`.

## Check the port against the GNC team's results

```
build\shield_gnc_sitl.exe --validate ..\..\External\SHIELD-S_GNC_PIPELINE\docs\testing\shield_simulation\unity_trajectory.csv
build\shield_gnc_sitl.exe --selftest
```

- `--validate` replays the reference trajectory from one of their
  Simscape runs (full plant, including the averaged ESC and battery)
  through this port with the same gain, and compares positions. Against
  their reviewed `P4_R10` run it agrees to 0.2 mm max, 0.1 mm RMS, over
  17 s. Fails above `--tolerance` (default 0.05 m).
- `--selftest` checks the hover trim is an equilibrium and flies moves of
  up to 20 m, passing only if each arrives within 2 cm inside their 30 deg
  tilt and 8 m/s speed screens.

Run either after the GNC team updates their gains, parameters or model.

## Run

```
build\shield_gnc_sitl.exe [--controller P4_R10] [--command-port 5561] [--move-on] [--quit-on-stdin]
```

Line-based TCP commands on `127.0.0.1:5561`; positions are in Unity axes
(X right, Y up, Z forward) relative to where the vehicle started:

| Command | Effect |
|---|---|
| `MOVE ON` / `MOVE OFF` | Run / freeze the simulation. Starts frozen unless `--move-on`. |
| `GOTO <x> <y> <z> [speed]` | Fly to a position (default cruise 2 m/s). |
| `HOLD` | Stop where the reference can brake to. |
| `RESET` | Back to hover at the origin. |
| `QUIT` | Exit. |

State is logged ten times a second:
`[GNC] state t=... move=ON mode=GOTO pos=(x,y,z) vel=(...) att=(roll,pitch,yaw)deg ref=(...)`.
If the vehicle diverges (tilt past 75 deg) it logs an `ERROR` and freezes
until `RESET`.

## Placeholder reference shaping

The GNC repo has no guidance yet. The LQR was tuned and screened on
position steps of at most 1 m, so `guidance.hpp` moves the position
reference toward the `GOTO` goal at a bounded speed and acceleration
(2 m/s, 1.5 m/s^2 by default) and hands the LQR the matching velocity,
like `makeTrajectory` does. Replace it with the GNC team's guidance once
it exists. Yaw is held at 0: the LQR was linearized at zero yaw.

## Fidelity

Same boundary as the GNC team's design model: ideal state feedback (no
estimator or sensor noise), no ESC current/duty or battery dynamics, no
drag, wind, ground contact or communication delay. Thrust/torque
coefficients and motor limits are their provisional values. Passing
`--validate` shows the port matches their simulation, not that either
matches the aircraft.
