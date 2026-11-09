#include "vehicle_model.hpp"

#include <algorithm>
#include <cmath>
#include <fstream>
#include <regex>
#include <sstream>
#include <stdexcept>

namespace shield_sitl {
namespace {

std::string ReadFile(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error("Cannot open " + path);
    std::ostringstream ss;
    ss << in.rdbuf();
    return ss.str();
}

// The "P" block is a flat object of numbers, so a brace match plus a
// "name": number scan is enough - no general JSON parser needed.
std::string ObjectBody(const std::string& json, const std::string& key) {
    size_t k = json.find("\"" + key + "\"");
    if (k == std::string::npos) throw std::runtime_error("No \"" + key + "\" object in parameters file");
    size_t open = json.find('{', k);
    size_t close = json.find('}', open);
    if (open == std::string::npos || close == std::string::npos)
        throw std::runtime_error("Malformed \"" + key + "\" object in parameters file");
    return json.substr(open + 1, close - open - 1);
}

double Field(const std::string& body, const std::string& name) {
    std::regex re("\"" + name + "\"\\s*:\\s*(-?[0-9.eE+-]+)");
    std::smatch m;
    if (!std::regex_search(body, m, re)) throw std::runtime_error("Missing parameter P." + name);
    return std::stod(m[1].str());
}

}  // namespace

VehicleParams LoadVehicleParams(const std::string& path) {
    const std::string body = ObjectBody(ReadFile(path), "P");
    VehicleParams P;
    P.m = Field(body, "m");
    P.g = Field(body, "g");
    P.Jx = Field(body, "Jx");
    P.Jy = Field(body, "Jy");
    P.Jz = Field(body, "Jz");
    P.armX_m = Field(body, "armX_m");
    P.armY_m = Field(body, "armY_m");
    P.kf = Field(body, "kf");
    P.km = Field(body, "km");
    P.Jr = Field(body, "Jr");
    P.tau_m = Field(body, "tau_m");
    P.OmegaMax = Field(body, "OmegaMax");
    for (double v : {P.m, P.g, P.Jx, P.Jy, P.Jz, P.armX_m, P.armY_m, P.kf, P.km, P.Jr, P.tau_m, P.OmegaMax})
        if (!(v > 0)) throw std::runtime_error("Vehicle parameters must all be positive");
    return P;
}

std::array<double, 4> LoadHoverInput(const std::string& path, bool* found) {
    std::array<double, 4> uh{};
    *found = false;
    const std::string json = ReadFile(path);
    std::smatch m;
    if (!std::regex_search(json, m, std::regex("\"uh\"\\s*:\\s*\\[([^\\]]*)\\]"))) return uh;
    std::stringstream ss(m[1].str());
    std::string item;
    int i = 0;
    while (i < 4 && std::getline(ss, item, ',')) uh[i++] = std::stod(item);
    *found = (i == 4);
    return uh;
}

double HoverRotorSpeed(const VehicleParams& P) { return std::sqrt(P.m * P.g / (4.0 * P.kf)); }

std::array<double, 9> Rotation(double f, double t, double p) {
    const double cf = std::cos(f), sf = std::sin(f);
    const double ct = std::cos(t), st = std::sin(t);
    const double cp = std::cos(p), sp = std::sin(p);
    return {cp * ct, cp * st * sf - sp * cf, cp * st * cf + sp * sf,
            sp * ct, sp * st * sf + cp * cf, sp * st * cf - cp * sf,
            -st,     ct * sf,                ct * cf};
}

State Derivative(const State& x, const Input& u, const VehicleParams& P) {
    // Rotor response: (min(max(u,0),OmegaMax) - Omega) / tau, as in verifyEquations.
    std::array<double, 4> w{}, wd{}, f{};
    for (int i = 0; i < 4; ++i) {
        w[i] = x[12 + i];
        wd[i] = (std::clamp(u[i], 0.0, P.OmegaMax) - w[i]) / P.tau_m;
        f[i] = P.kf * w[i] * w[i];
    }
    const double T = f[0] + f[1] + f[2] + f[3];
    const double p = x[9], q = x[10], r = x[11];

    // Rotor spin directions -1, 1, -1, 1 (FL, FR, RR, RL).
    const double h = P.Jr * (-w[0] + w[1] - w[2] + w[3]);
    const double hd = P.Jr * (-wd[0] + wd[1] - wd[2] + wd[3]);
    const double tauX = P.armY_m * (f[0] - f[1] - f[2] + f[3]) - h * q;
    const double tauY = P.armX_m * (-f[0] - f[1] + f[2] + f[3]) + h * p;
    const double tauZ = P.km * (w[0] * w[0] - w[1] * w[1] + w[2] * w[2] - w[3] * w[3]) - hd;

    const double roll = x[6], pitch = x[7], yaw = x[8];
    const auto R = Rotation(roll, pitch, yaw);
    const double sf = std::sin(roll), cf = std::cos(roll), tt = std::tan(pitch), ct = std::cos(pitch);

    State dx{};
    dx[0] = x[3];
    dx[1] = x[4];
    dx[2] = x[5];
    dx[3] = R[2] * T / P.m;  // third column of R times T/m, minus gravity on z
    dx[4] = R[5] * T / P.m;
    dx[5] = R[8] * T / P.m - P.g;
    dx[6] = p + sf * tt * q + cf * tt * r;
    dx[7] = cf * q - sf * r;
    dx[8] = (sf * q + cf * r) / ct;
    // J \ (tau - w x (J w)), J diagonal.
    dx[9] = (tauX - (q * P.Jz * r - r * P.Jy * q)) / P.Jx;
    dx[10] = (tauY - (r * P.Jx * p - p * P.Jz * r)) / P.Jy;
    dx[11] = (tauZ - (p * P.Jy * q - q * P.Jx * p)) / P.Jz;
    for (int i = 0; i < 4; ++i) dx[12 + i] = wd[i];
    return dx;
}

void Rk4Step(State& x, const Input& u, const VehicleParams& P, double dt) {
    auto add = [](const State& a, const State& b, double s) {
        State c{};
        for (size_t i = 0; i < a.size(); ++i) c[i] = a[i] + s * b[i];
        return c;
    };
    const State k1 = Derivative(x, u, P);
    const State k2 = Derivative(add(x, k1, dt / 2), u, P);
    const State k3 = Derivative(add(x, k2, dt / 2), u, P);
    const State k4 = Derivative(add(x, k3, dt), u, P);
    for (size_t i = 0; i < x.size(); ++i) x[i] += dt * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]) / 6.0;
}

State HoverState(const VehicleParams& P, double px, double py, double pz) {
    State x{};
    x[0] = px;
    x[1] = py;
    x[2] = pz;
    const double wh = HoverRotorSpeed(P);
    for (int i = 0; i < 4; ++i) x[12 + i] = wh;
    return x;
}

}  // namespace shield_sitl
