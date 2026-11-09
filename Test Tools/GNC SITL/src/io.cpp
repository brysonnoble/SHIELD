#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <winsock2.h>
#include <ws2tcpip.h>

#include "io.hpp"

#include <cstdarg>
#include <cstdio>
#include <mutex>

namespace shield_sitl {
namespace {

std::mutex g_logMutex;

SOCKET S(uintptr_t s) { return static_cast<SOCKET>(s); }

bool SetNonBlocking(SOCKET s) {
    u_long on = 1;
    return ioctlsocket(s, FIONBIO, &on) == 0;
}

}  // namespace

void Log(const char* fmt, ...) {
    std::lock_guard<std::mutex> lock(g_logMutex);
    va_list args;
    va_start(args, fmt);
    std::vfprintf(stdout, fmt, args);
    va_end(args);
    std::fputc('\n', stdout);
    std::fflush(stdout);
}

bool NetInit() {
    WSADATA data;
    return WSAStartup(MAKEWORD(2, 2), &data) == 0;
}

void NetShutdown() { WSACleanup(); }

CommandServer::~CommandServer() {
    for (auto& c : clients_) closesocket(S(c.sock));
    if (S(listen_) != INVALID_SOCKET) closesocket(S(listen_));
}

bool CommandServer::Start(int port) {
    SOCKET s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (s == INVALID_SOCKET) return false;
    // Exclusive bind, so a leftover SITL from an earlier run makes this one
    // fail loudly instead of both silently sharing the port.
    BOOL exclusive = TRUE;
    setsockopt(s, SOL_SOCKET, SO_EXCLUSIVEADDRUSE, reinterpret_cast<const char*>(&exclusive), sizeof(exclusive));
    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port = htons(static_cast<u_short>(port));
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    if (bind(s, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) != 0 || listen(s, 8) != 0 || !SetNonBlocking(s)) {
        closesocket(s);
        return false;
    }
    listen_ = static_cast<uintptr_t>(s);
    return true;
}

std::vector<std::string> CommandServer::Poll() {
    std::vector<std::string> lines;
    if (S(listen_) == INVALID_SOCKET) return lines;

    for (;;) {
        SOCKET c = accept(S(listen_), nullptr, nullptr);
        if (c == INVALID_SOCKET) break;
        SetNonBlocking(c);
        clients_.push_back({static_cast<uintptr_t>(c), {}});
    }

    char buf[1024];
    for (size_t i = 0; i < clients_.size();) {
        Client& client = clients_[i];
        bool closed = false;
        for (;;) {
            int n = recv(S(client.sock), buf, sizeof(buf), 0);
            if (n > 0) {
                client.buffer.append(buf, n);
                continue;
            }
            if (n == 0 || WSAGetLastError() != WSAEWOULDBLOCK) closed = true;
            break;
        }
        size_t nl;
        while ((nl = client.buffer.find('\n')) != std::string::npos) {
            std::string line = client.buffer.substr(0, nl);
            client.buffer.erase(0, nl + 1);
            while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
            if (!line.empty()) lines.push_back(line);
        }
        if (closed) {
            if (!client.buffer.empty()) lines.push_back(client.buffer);  // last line without a newline
            closesocket(S(client.sock));
            clients_.erase(clients_.begin() + i);
        } else {
            ++i;
        }
    }
    return lines;
}

}  // namespace shield_sitl
