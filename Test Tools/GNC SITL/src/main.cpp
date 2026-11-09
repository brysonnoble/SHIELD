// SHIELD GNC SITL: runs the GNC team's LQR flight controller against the
// model it was designed on, in real time, driven by position commands
// (MOVE/GOTO/HOLD) over a local TCP port. Also checks the port against the
// GNC team's own Simscape results (--selftest, --validate). See README.md
// next to src/.

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <timeapi.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <deque>
#include <fstream>
#include <iostream>
#include <map>
#include <regex>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

#include "guidance.hpp"
#include "io.hpp"
#include "lqr_controller.hpp"
#include "vec3.hpp"
#include "vehicle_model.hpp"

using namespace shield_sitl;

namespace {

constexpr double kPi = 3.14159265358979323846;
constexpr double kRad2Deg = 180.0 / kPi;
constexpr int kPhysicsStepsPerControl = 5;  // RK4 substeps per controller sample
constexpr double kDivergedTiltDeg = 75.0;   // well past anything the LQR was screened on

struct Options {
    std::string gncRoot;
    std::string controller = "P4_R10";  // the case runShieldSimulation selected (TEST_REPORT.md)
    int commandPort = 5561;
    bool moveOn = false;
    bool quitOnStdin = false;
    double maxSpeed = 2.0;
    double maxAccel = 1.5;
    std::string validateCsv;
    double tolerance = 0.05;
    bool selftest = false;
};

std::string ExeDirectory() {
    char path[MAX_PATH];
    DWORD n = GetModuleFileNameA(nullptr, path, MAX_PATH);
    std::string s(path, n);
    return s.substr(0, s.find_last_of("\\/"));
}

void Usage() {
    std::printf(
        "shield_gnc_sitl [--gnc-root DIR] [--controller CASE] [--move-on] [--quit-on-stdin]\n"
        "                [--command-port N]\n"
        "                [--max-speed M_S] [--max-accel M_S2]\n"
        "shield_gnc_sitl --selftest [--gnc-root DIR] [--controller CASE]\n"
        "shield_gnc_sitl --validate unity_trajectory.csv [--tolerance M] [--gnc-root DIR] [--controller CASE]\n");
}

Options ParseArgs(int argc, char** argv) {
    Options o;
    o.gncRoot = ExeDirectory() + "\\..\\..\\..\\External\\SHIELD-S_GNC_PIPELINE";
    for (int i = 1; i < argc; ++i) {
        std::string a = argv[i];
        auto next = [&]() -> std::string {
            if (i + 1 >= argc) throw std::runtime_error("Missing value after " + a);
            return argv[++i];
        };
        if (a == "--gnc-root") o.gncRoot = next();
        else if (a == "--controller") o.controller = next();
        else if (a == "--command-port") o.commandPort = std::stoi(next());
        else if (a == "--move-on") o.moveOn = true;
        else if (a == "--quit-on-stdin") o.quitOnStdin = true;
        else if (a == "--max-speed") o.maxSpeed = std::stod(next());
        else if (a == "--max-accel") o.maxAccel = std::stod(next());
        else if (a == "--validate") o.validateCsv = next();
        else if (a == "--tolerance") o.tolerance = std::stod(next());
        else if (a == "--selftest") o.selftest = true;
        else if (a == "--help" || a == "-h") { Usage(); std::exit(0); }
        else throw std::runtime_error("Unknown argument " + a);
    }
    if (!(o.maxSpeed > 0) || !(o.maxAccel > 0)) throw std::runtime_error("--max-speed and --max-accel must be positive");
    return o;
}

// The GNC team's reviewed reference run lives in their repo; the SITL reads
// it as-is rather than keeping its own copy of their numbers.
std::string ResultsDir(const Options& o) { return o.gncRoot + "\\docs\\testing\\shield_simulation"; }

double LoadSampleTime(const Options& o) {
    std::ifstream in(ResultsDir(o) + "\\configuration.json");
    if (!in) throw std::runtime_error("Cannot open " + ResultsDir(o) + "\\configuration.json");
    std::stringstream ss;
    ss << in.rdbuf();
    std::smatch m;
    const std::string json = ss.str();
    if (!std::regex_search(json, m, std::regex("\"sampleTime_s\"\\s*:\\s*([0-9.eE+-]+)")))
        throw std::runtime_error("configuration.json has no control.sampleTime_s");
    return std::stod(m[1].str());
}

struct Flight {
    VehicleParams P;
    LqrController lqr;
    double Ts;
    double dt;
};

Flight LoadFlight(const Options& o) {
    const std::string params = ResultsDir(o) + "\\effective_parameters.json";
    VehicleParams P = LoadVehicleParams(params);
    const double hover = HoverRotorSpeed(P);
    bool found = false;
    auto uh = LoadHoverInput(params, &found);
    if (found && std::fabs(uh[0] - hover) > 1e-6 * hover)
        throw std::runtime_error("Hover rotor speed computed from P does not match the file's uh");
    const double Ts = LoadSampleTime(o);
    return {P, LqrController(ResultsDir(o) + "\\gain_" + o.controller + ".csv", hover), Ts, Ts / kPhysicsStepsPerControl};
}

Vec3 Position(const State& x) { return {x[0], x[1], x[2]}; }
Vec3 Velocity(const State& x) { return {x[3], x[4], x[5]}; }

double TiltDeg(const State& x) {
    return std::acos(std::clamp(std::cos(x[6]) * std::cos(x[7]), -1.0, 1.0)) * kRad2Deg;
}

bool Diverged(const State& x) {
    for (double v : x)
        if (!std::isfinite(v)) return true;
    return TiltDeg(x) > kDivergedTiltDeg;
}

// One controller sample: guidance reference -> LQR command -> RK4 substeps.
void ControlStep(const Flight& f, State& x, ReferenceGenerator& ref) {
    ref.Step(f.Ts);
    const State r = f.lqr.Reference(ref.Position(), ref.Velocity(), 0.0);
    const Input u = f.lqr.Command(x, r);
    for (int k = 0; k < kPhysicsStepsPerControl; ++k) Rk4Step(x, u, f.P, f.dt);
}

// ---------------------------------------------------------------- selftest

struct MoveResult {
    double finalError, peakTiltDeg, peakSpeed;
    bool diverged;
};

MoveResult SimulateMove(const Flight& f, const Options& o, const Vec3& goal, double seconds) {
    State x = HoverState(f.P, 0, 0, 0);
    ReferenceGenerator ref;
    ref.Reset({});
    ref.SetLimits(o.maxSpeed, o.maxAccel);
    ref.SetGoal(goal);
    MoveResult r{0, 0, 0, false};
    for (double t = 0; t < seconds; t += f.Ts) {
        ControlStep(f, x, ref);
        if (Diverged(x)) {
            r.diverged = true;
            break;
        }
        r.peakTiltDeg = std::max(r.peakTiltDeg, TiltDeg(x));
        r.peakSpeed = std::max(r.peakSpeed, Norm(Velocity(x)));
    }
    r.finalError = Norm(Position(x) - goal);
    return r;
}

int RunSelftest(const Options& o) {
    Flight f = LoadFlight(o);
    bool ok = true;
    auto check = [&](bool pass, const char* what) {
        Log("%s %s", pass ? "PASS" : "FAIL", what);
        ok = ok && pass;
    };

    const State h = HoverState(f.P, 0, 0, 0);
    const Input uh{f.lqr.HoverSpeed(), f.lqr.HoverSpeed(), f.lqr.HoverSpeed(), f.lqr.HoverSpeed()};
    double maxDeriv = 0;
    for (double v : Derivative(h, uh, f.P)) maxDeriv = std::max(maxDeriv, std::fabs(v));
    Log("hover rotor speed %.4f rad/s, |dx/dt| at trim %.3g", f.lqr.HoverSpeed(), maxDeriv);
    check(maxDeriv < 1e-8, "trim is an equilibrium (verifyEquations' 1e-8 bound)");

    // The runShieldSimulation screens: 30 deg tilt, 8 m/s speed.
    struct Case {
        const char* name;
        Vec3 goalSim;
    } cases[] = {
        {"hold position 10 s", {0, 0, 0}},
        {"1 m forward step", {1, 0, 0}},
        {"10 m forward", {10, 0, 0}},
        {"5 m left, 2 m up", {0, 5, 2}},
        {"20 m diagonal climb", {12, -12, 10}},
    };
    for (const auto& c : cases) {
        const double seconds = Norm(c.goalSim) / o.maxSpeed + o.maxSpeed / o.maxAccel + 8.0;
        MoveResult r = SimulateMove(f, o, c.goalSim, std::max(seconds, 10.0));
        Log("%-22s final error %.4f m, peak tilt %.1f deg, peak speed %.2f m/s%s", c.name, r.finalError,
            r.peakTiltDeg, r.peakSpeed, r.diverged ? " DIVERGED" : "");
        check(!r.diverged && r.finalError < 0.02 && r.peakTiltDeg <= 30.0 && r.peakSpeed <= 8.0,
              "  arrives within 2 cm, tilt <= 30 deg, speed <= 8 m/s");
    }
    Log(ok ? "SELFTEST PASS" : "SELFTEST FAIL");
    return ok ? 0 : 1;
}

// ---------------------------------------------------------------- validate

// Replays the reference from one of runShieldSimulation's Unity CSVs (the
// full Simscape plant under the same gain) through this port and compares.
int RunValidate(const Options& o) {
    Flight f = LoadFlight(o);
    std::ifstream in(o.validateCsv);
    if (!in) throw std::runtime_error("Cannot open " + o.validateCsv);
    std::string line;
    std::getline(in, line);  // header
    std::vector<std::vector<double>> rows;
    while (std::getline(in, line)) {
        if (line.empty()) continue;
        std::vector<double> row;
        std::stringstream ss(line);
        std::string cell;
        while (std::getline(ss, cell, ',')) row.push_back(std::stod(cell));
        if (row.size() < 20) throw std::runtime_error("Expected at least 20 columns in " + o.validateCsv);
        rows.push_back(row);
    }
    if (rows.size() < 3) throw std::runtime_error("Too few rows in " + o.validateCsv);
    const double step = rows[1][0] - rows[0][0];
    if (std::fabs(step - f.Ts) > 1e-9)
        throw std::runtime_error("CSV sample time does not match the controller sample time");

    // The CSV keeps reference position only; the run supplied velocity too
    // (makeTrajectory), so recover it by central differences.
    const size_t n = rows.size();
    auto refPos = [&](size_t i) { return Vec3{rows[i][17], rows[i][18], rows[i][19]}; };
    auto refVel = [&](size_t i) {
        size_t a = i == 0 ? 0 : i - 1, b = i + 1 < n ? i + 1 : n - 1;
        return (refPos(b) - refPos(a)) * (1.0 / ((b - a) * step));
    };

    State x{};
    for (int j = 0; j < 16; ++j) x[j] = rows[0][1 + j];
    double maxPos = 0, maxAtt = 0, sumSq = 0;
    for (size_t i = 0; i + 1 < n; ++i) {
        const Input u = f.lqr.Command(x, f.lqr.Reference(refPos(i), refVel(i), 0.0));
        for (int k = 0; k < kPhysicsStepsPerControl; ++k) Rk4Step(x, u, f.P, f.dt);
        const std::vector<double>& s = rows[i + 1];
        const double e = Norm(Position(x) - Vec3{s[1], s[2], s[3]});
        maxPos = std::max(maxPos, e);
        sumSq += e * e;
        for (int j = 6; j < 9; ++j) maxAtt = std::max(maxAtt, std::fabs(x[j] - s[1 + j]) * kRad2Deg);
    }
    const double rms = std::sqrt(sumSq / (n - 1));
    Log("Replayed %zu samples (%.2f s) of %s with gain %s", n, (n - 1) * step, o.validateCsv.c_str(),
        o.controller.c_str());
    Log("Position vs Simscape: max %.4f m, RMS %.4f m; attitude max %.3f deg", maxPos, rms, maxAtt);
    const bool ok = maxPos <= o.tolerance;
    Log(ok ? "VALIDATE PASS (max position error <= %.3f m)" : "VALIDATE FAIL (max position error > %.3f m)",
        o.tolerance);
    return ok ? 0 : 1;
}

// --------------------------------------------------------------------- run

enum class Mode { Hold, Goto };
const char* ModeName(Mode m) { return m == Mode::Hold ? "HOLD" : "GOTO"; }

class Sitl {
public:
    Sitl(const Options& o, Flight f) : o_(o), f_(std::move(f)) {
        moving_ = o.moveOn;
        Reset();
    }

    void Reset() {
        x_ = HoverState(f_.P, 0, 0, 0);
        ref_.Reset({});
        ref_.SetLimits(o_.maxSpeed, o_.maxAccel);
        mode_ = Mode::Hold;
        diverged_ = false;
    }

    // Returns false on QUIT.
    bool HandleCommand(const std::string& line) {
        std::istringstream in(line);
        std::string verb;
        in >> verb;
        std::transform(verb.begin(), verb.end(), verb.begin(), ::toupper);
        std::string error;
        if (verb == "QUIT") {
            Log("[GNC] cmd '%s' -> OK", line.c_str());
            return false;
        } else if (verb == "MOVE") {
            std::string arg;
            in >> arg;
            std::transform(arg.begin(), arg.end(), arg.begin(), ::toupper);
            if (arg == "ON") {
                if (diverged_) error = "vehicle diverged; send RESET first";
                else moving_ = true;
            } else if (arg == "OFF") {
                moving_ = false;
            } else {
                error = "expected MOVE ON|OFF";
            }
        } else if (verb == "HOLD") {
            Hold();
        } else if (verb == "GOTO") {
            double ux, uy, uz, speed = o_.maxSpeed;
            if (!(in >> ux >> uy >> uz)) error = "expected GOTO <x> <y> <z> [speed] (Unity coordinates)";
            else {
                if (!(in >> speed)) speed = o_.maxSpeed;
                if (!(speed > 0)) error = "speed must be positive";
                else {
                    mode_ = Mode::Goto;
                    ref_.SetLimits(speed, o_.maxAccel);
                    ref_.SetGoal(UnityToSim({ux, uy, uz}));
                }
            }
        } else if (verb == "RESET") {
            Reset();
        } else {
            error = "unknown command";
        }
        if (error.empty()) Log("[GNC] cmd '%s' -> OK", line.c_str());
        else Log("[GNC] cmd '%s' -> ERROR %s", line.c_str(), error.c_str());
        return true;
    }

    // Advances the simulation by real elapsed time while movement is on.
    void Advance(double seconds) {
        if (!moving_) {
            accumulator_ = 0;
            return;
        }
        accumulator_ = std::min(accumulator_ + seconds, 0.25);  // don't spiral after a stall
        while (accumulator_ >= f_.Ts) {
            accumulator_ -= f_.Ts;
            ControlStep(f_, x_, ref_);
            simTime_ += f_.Ts;
            if (Diverged(x_)) {
                Log("[GNC] ERROR vehicle diverged (tilt %.1f deg) - movement stopped; send RESET to recover",
                    TiltDeg(x_));
                diverged_ = true;
                moving_ = false;
                accumulator_ = 0;
                return;
            }
        }
    }

    void LogState() const {
        const Vec3 p = SimToUnity(Position(x_)), v = SimToUnity(Velocity(x_)), r = SimToUnity(ref_.Position());
        Log("[GNC] state t=%.2f move=%s mode=%s pos=(%.3f,%.3f,%.3f) vel=(%.3f,%.3f,%.3f) "
            "att=(%.1f,%.1f,%.1f)deg ref=(%.3f,%.3f,%.3f)",
            simTime_, moving_ ? "ON" : "OFF", ModeName(mode_), p.x, p.y, p.z, v.x, v.y, v.z, x_[6] * kRad2Deg,
            x_[7] * kRad2Deg, x_[8] * kRad2Deg, r.x, r.y, r.z);
    }

private:
    void Hold() {
        mode_ = Mode::Hold;
        ref_.SetGoal(ref_.StoppingPoint());
    }

    Options o_;
    Flight f_;
    State x_{};
    ReferenceGenerator ref_;
    Mode mode_ = Mode::Hold;
    bool moving_ = false;
    bool diverged_ = false;
    double accumulator_ = 0;
    double simTime_ = 0;
};

std::atomic<bool> g_quit{false};

BOOL WINAPI OnConsoleCtrl(DWORD) {
    g_quit = true;
    return TRUE;
}

int RunSitl(const Options& o) {
    Flight flight = LoadFlight(o);
    if (!NetInit()) throw std::runtime_error("WSAStartup failed");

    CommandServer commands;
    if (!commands.Start(o.commandPort))
        throw std::runtime_error("Could not bind command port " + std::to_string(o.commandPort) +
                                 " (is another SITL still running?)");

    SetConsoleCtrlHandler(OnConsoleCtrl, TRUE);
    if (o.quitOnStdin) {
        std::thread([] {
            std::string line;
            while (std::getline(std::cin, line)) {
                line.erase(std::remove(line.begin(), line.end(), '\r'), line.end());
                if (line == "QUIT") break;
            }
            g_quit = true;
        }).detach();
    }

    Sitl sitl(o, flight);
    Log("[GNC] SITL ready: controller=%s Ts=%.4fs hover=%.3frad/s move=%s commands=tcp:%d",
        o.controller.c_str(), flight.Ts, flight.lqr.HoverSpeed(), o.moveOn ? "ON" : "OFF", o.commandPort);
    Log("[GNC] Position reference is a PLACEHOLDER rate limiter, not the GNC team's guidance.");

    timeBeginPeriod(1);
    using clock = std::chrono::steady_clock;
    auto last = clock::now();
    auto nextLog = last;
    while (!g_quit) {
        const auto now = clock::now();
        for (const auto& line : commands.Poll())
            if (!sitl.HandleCommand(line)) g_quit = true;

        sitl.Advance(std::chrono::duration<double>(now - last).count());
        last = now;

        if (now >= nextLog) {
            sitl.LogState();
            nextLog = now + std::chrono::milliseconds(100);
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    timeEndPeriod(1);
    Log("[GNC] Shutting down.");
    NetShutdown();
    return 0;
}

}  // namespace

int main(int argc, char** argv) {
    try {
        Options o = ParseArgs(argc, argv);
        if (o.selftest) return RunSelftest(o);
        if (!o.validateCsv.empty()) return RunValidate(o);
        return RunSitl(o);
    } catch (const std::exception& e) {
        Log("[GNC] ERROR %s", e.what());
        return 2;
    }
}
