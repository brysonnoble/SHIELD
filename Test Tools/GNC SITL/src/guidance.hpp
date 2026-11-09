#pragma once

// PLACEHOLDER GUIDANCE - not the GNC team's guidance law.
//
// The GNC repo has an LQR position controller but no guidance yet (its
// core/src/guidance is empty). The LQR was designed and screened on
// position steps of at most 1 m, so a large jump in its reference would
// drive it far outside the linear region it was tuned for. This shapes
// the reference instead: it moves toward a goal at no more than a cruise
// speed and acceleration, and hands the LQR both the position and the
// matching velocity, like makeTrajectory() in runShieldSimulation.m.
//
// Replace this file with the real guidance once it exists.

#include <algorithm>
#include <cmath>

#include "vec3.hpp"

namespace shield_sitl {

class ReferenceGenerator {
public:
    void Reset(const Vec3& position) {
        pos_ = position;
        vel_ = {};
        goal_ = position;
    }

    void SetGoal(const Vec3& goal) { goal_ = goal; }
    void SetLimits(double maxSpeed, double maxAccel) {
        maxSpeed_ = maxSpeed;
        maxAccel_ = maxAccel;
    }

    // Advances the reference by dt seconds: accelerate toward the goal at
    // up to maxAccel, cruise at up to maxSpeed, and brake so it stops on it.
    void Step(double dt) {
        const Vec3 toGoal = goal_ - pos_;
        const double dist = Norm(toGoal);
        const double speed = std::min(maxSpeed_, std::sqrt(2.0 * maxAccel_ * dist));
        const Vec3 desired = dist > 1e-9 ? toGoal * (speed / dist) : Vec3{};
        Vec3 dv = desired - vel_;
        const double dvNorm = Norm(dv), limit = maxAccel_ * dt;
        if (dvNorm > limit) dv = dv * (limit / dvNorm);
        vel_ = vel_ + dv;
        pos_ = pos_ + vel_ * dt;
        if (dist < 1e-3 && Norm(vel_) <= limit) {
            pos_ = goal_;
            vel_ = {};
        }
    }

    // Where the reference comes to rest if it brakes as hard as allowed now.
    Vec3 StoppingPoint() const {
        const double speed = Norm(vel_);
        return speed > 0 ? pos_ + vel_ * (speed / (2.0 * maxAccel_)) : pos_;
    }

    const Vec3& Position() const { return pos_; }
    const Vec3& Velocity() const { return vel_; }
    const Vec3& Goal() const { return goal_; }
    double MaxSpeed() const { return maxSpeed_; }

private:
    Vec3 pos_, vel_, goal_;
    double maxSpeed_ = 2.0;
    double maxAccel_ = 1.5;
};

}  // namespace shield_sitl
