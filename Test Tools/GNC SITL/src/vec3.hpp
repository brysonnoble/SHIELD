#pragma once

#include <cmath>

namespace shield_sitl {

struct Vec3 {
    double x = 0.0;
    double y = 0.0;
    double z = 0.0;
};

inline Vec3 operator+(const Vec3& a, const Vec3& b) { return {a.x + b.x, a.y + b.y, a.z + b.z}; }
inline Vec3 operator-(const Vec3& a, const Vec3& b) { return {a.x - b.x, a.y - b.y, a.z - b.z}; }
inline Vec3 operator*(const Vec3& a, double s) { return {a.x * s, a.y * s, a.z * s}; }
inline double Norm(const Vec3& a) { return std::sqrt(a.x * a.x + a.y * a.y + a.z * a.z); }
inline bool IsFinite(const Vec3& a) { return std::isfinite(a.x) && std::isfinite(a.y) && std::isfinite(a.z); }

// The GNC simulation frame (runShieldSimulation.m exportReplay) is X forward,
// Y left, Z up. Unity is X right, Y up, Z forward. Same mapping as the GNC
// repo's simulation/unity/Editor/ShieldSimulationReplay.cs: (-y, z, x).
// GOTO commands and logged positions use Unity axes, so they read in the
// same frame as the STE test scenes.
inline Vec3 SimToUnity(const Vec3& s) { return {-s.y, s.z, s.x}; }
inline Vec3 UnityToSim(const Vec3& u) { return {u.z, -u.x, u.y}; }

}  // namespace shield_sitl
