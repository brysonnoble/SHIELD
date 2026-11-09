#pragma once

// Windows (Winsock) plumbing: logging, and the line-based TCP command
// server (bound to 127.0.0.1 only) that drives the SITL.

#include <cstdint>
#include <string>
#include <vector>

namespace shield_sitl {

// Thread-safe, flushed line to stdout.
void Log(const char* fmt, ...);

bool NetInit();
void NetShutdown();

class CommandServer {
public:
    ~CommandServer();
    bool Start(int port);
    // Complete lines received since the last call.
    std::vector<std::string> Poll();

private:
    struct Client {
        uintptr_t sock;
        std::string buffer;
    };
    uintptr_t listen_ = ~uintptr_t(0);
    std::vector<Client> clients_;
};

}  // namespace shield_sitl
