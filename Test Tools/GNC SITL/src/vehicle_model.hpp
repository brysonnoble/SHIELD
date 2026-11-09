#pragma once

// C++ port of the 16-state quadrotor model from the GNC repo's
// simulation/matlab_simulink/runShieldSimulation.m (rigidDerivative,
// rotation, and the first-order rotor response used by verifyEquations).
// This is the same model the LQR gains were designed against. It is NOT
// the full Simscape plant: the averaged ESC current/duty dynamics and the
// battery are left out (see README.md, "Fidelity").
//
// No platform-specific code in here, so it can move into the GNC repo's
// planned portable core/ library unchanged.

#include <array>
#include <string>

namespace shield_sitl {

// x, y, z, vx, vy, vz (world), roll, pitch, yaw (rad), p, q, r (body rad/s),
// then the four ACTUAL rotor speeds FL, FR, RR, RL (rad/s).
// World frame: X forward, Y left, Z up.
using State = std::array<double, 16>;

// Four COMMANDED rotor speeds FL, FR, RR, RL (rad/s).
using Input = std::array<double, 4>;

struct VehicleParams {
    double m = 0, g = 0;
    double Jx = 0, Jy = 0, Jz = 0;
    double armX_m = 0, armY_m = 0;
    double kf = 0, km = 0;
    double Jr = 0;
    double tau_m = 0;
    double OmegaMax = 0;
};

// Reads the "P" block of a runShieldSimulation effective_parameters.json.
// Throws std::runtime_error on a missing file or field.
VehicleParams LoadVehicleParams(const std::string& effectiveParametersPath);

// Reads that file's "uh" (hover rotor speeds) for cross-checking; empty if absent.
std::array<double, 4> LoadHoverInput(const std::string& effectiveParametersPath, bool* found);

// sqrt(m g / (4 kf)), as trimState() computes it.
double HoverRotorSpeed(const VehicleParams& P);

// Body-to-world rotation (row-major 3x3), ZYX Euler, as rotation() in the .m file.
std::array<double, 9> Rotation(double roll, double pitch, double yaw);

// dx/dt for the rigid body plus first-order rotor response to the clamped command.
State Derivative(const State& x, const Input& u, const VehicleParams& P);

// One classical RK4 step with the command held constant.
void Rk4Step(State& x, const Input& u, const VehicleParams& P, double dt);

// Initial hover state at the given world position, level, rotors at hover speed.
State HoverState(const VehicleParams& P, double px, double py, double pz);

}  // namespace shield_sitl
