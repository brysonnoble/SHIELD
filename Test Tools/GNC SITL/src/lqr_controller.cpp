#include "lqr_controller.hpp"

#include <fstream>
#include <sstream>
#include <stdexcept>

namespace shield_sitl {

LqrController::LqrController(const std::string& gainCsvPath, double hoverRotorSpeed) : hover_(hoverRotorSpeed) {
    std::ifstream in(gainCsvPath);
    if (!in) throw std::runtime_error("Cannot open gain file " + gainCsvPath);
    std::string line;
    int row = 0;
    while (std::getline(in, line)) {
        if (line.find_first_not_of(" \t\r\n") == std::string::npos) continue;
        if (row >= 4) throw std::runtime_error("Gain file has more than 4 rows: " + gainCsvPath);
        std::stringstream ss(line);
        std::string cell;
        int col = 0;
        while (std::getline(ss, cell, ',')) {
            if (col >= 16) throw std::runtime_error("Gain file row has more than 16 columns: " + gainCsvPath);
            K_[row][col++] = std::stod(cell);
        }
        if (col != 16) throw std::runtime_error("Gain file row has fewer than 16 columns: " + gainCsvPath);
        ++row;
    }
    if (row != 4) throw std::runtime_error("Gain file must have 4 rows: " + gainCsvPath);
}

State LqrController::Reference(const Vec3& position, const Vec3& velocity, double yawRef) const {
    State ref{};
    ref[0] = position.x;
    ref[1] = position.y;
    ref[2] = position.z;
    ref[3] = velocity.x;
    ref[4] = velocity.y;
    ref[5] = velocity.z;
    ref[8] = yawRef;
    for (int i = 0; i < 4; ++i) ref[12 + i] = hover_;
    return ref;
}

Input LqrController::Command(const State& x, const State& ref) const {
    Input u{};
    for (int i = 0; i < 4; ++i) {
        double s = 0.0;
        for (int j = 0; j < 16; ++j) s += K_[i][j] * (x[j] - ref[j]);
        u[i] = hover_ - s;
    }
    return u;
}

}  // namespace shield_sitl
