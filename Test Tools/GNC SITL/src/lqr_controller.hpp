#pragma once

// The GNC repo's discrete LQR state feedback, u = uh - K (x - x_ref), with
// K read verbatim from a runShieldSimulation gain_<case>.csv (4 rows x 16
// columns, designed with dlqr at cfg.control.sampleTime_s). Same law as
// referenceSimulation() in runShieldSimulation.m. Portable C++, no platform code.

#include <array>
#include <string>

#include "vec3.hpp"
#include "vehicle_model.hpp"

namespace shield_sitl {

class LqrController {
public:
    // Loads K from a gain CSV. Throws std::runtime_error if it isn't 4x16.
    LqrController(const std::string& gainCsvPath, double hoverRotorSpeed);

    // Reference state: position/velocity from guidance, level attitude at
    // yawRef, zero body rates, rotors at hover speed (makeTrajectory's ref).
    State Reference(const Vec3& position, const Vec3& velocity, double yawRef) const;

    Input Command(const State& x, const State& ref) const;

    double HoverSpeed() const { return hover_; }

private:
    std::array<std::array<double, 16>, 4> K_{};
    double hover_;
};

}  // namespace shield_sitl
