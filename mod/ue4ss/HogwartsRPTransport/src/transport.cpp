#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <winhttp.h>
#include <lua.h>
#include <lauxlib.h>

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <regex>
#include <stdexcept>
#include <string>
#include <thread>

namespace {
constexpr DWORD kMaxMessageBytes = 32 * 1024;
constexpr auto kHeartbeatInterval = std::chrono::seconds(15);

struct State {
    std::mutex mutex;
    std::thread worker;
    std::atomic_bool stopping{false};
    std::string status{"idle"};
    std::string detail{"Transport is idle."};
    bool running{false};
};
State g_state;

void SetStatus(const std::string& status, const std::string& detail) {
    std::lock_guard lock(g_state.mutex);
    g_state.status = status;
    g_state.detail = detail;
}

std::string JsonEscape(const std::string& value) {
    std::string out;
    out.reserve(value.size() + 8);
    for (unsigned char c : value) {
        switch (c) {
        case '"': out += "\\\""; break;
        case '\\': out += "\\\\"; break;
        case '\b': out += "\\b"; break;
        case '\f': out += "\\f"; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default:
            if (c < 0x20) return {};
            out += static_cast<char>(c);
        }
    }
    return out;
}

std::string JsonOptional(const std::string& value) {
    return value.empty() ? "null" : "\"" + JsonEscape(value) + "\"";
}

bool HasType(const std::string& json, const std::string& type) {
    const std::regex pattern("\\\"type\\\"\\s*:\\s*\\\"" + type + "\\\"");
    return std::regex_search(json, pattern);
}

bool HasSequence(const std::string& json, std::int64_t sequence) {
    const std::regex pattern("\\\"sequence\\\"\\s*:\\s*" + std::to_string(sequence) + "(?:\\D|$)");
    return std::regex_search(json, pattern);
}

bool IsLoopbackHost(const std::wstring& host) {
    return _wcsicmp(host.c_str(), L"localhost") == 0 ||
           _wcsicmp(host.c_str(), L"127.0.0.1") == 0 ||
           _wcsicmp(host.c_str(), L"::1") == 0;
}

struct Endpoint {
    std::wstring host;
    INTERNET_PORT port{};
    std::wstring path;
    bool secure{};
};

bool ParseEndpoint(const std::string& input, Endpoint& endpoint) {
    if (input.empty() || input.size() > 2048) return false;
    const int needed = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, input.data(),
        static_cast<int>(input.size()), nullptr, 0);
    if (needed <= 0) return false;
    std::wstring wide(static_cast<size_t>(needed), L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, input.data(),
        static_cast<int>(input.size()), wide.data(), needed) != needed) return false;

    URL_COMPONENTS parts{};
    parts.dwStructSize = sizeof(parts);
    parts.dwSchemeLength = static_cast<DWORD>(-1);
    parts.dwHostNameLength = static_cast<DWORD>(-1);
    parts.dwUrlPathLength = static_cast<DWORD>(-1);
    parts.dwExtraInfoLength = static_cast<DWORD>(-1);
    if (!WinHttpCrackUrl(wide.c_str(), 0, 0, &parts)) return false;

    const std::wstring scheme(parts.lpszScheme, parts.dwSchemeLength);
    endpoint.host.assign(parts.lpszHostName, parts.dwHostNameLength);
    endpoint.path.assign(parts.lpszUrlPath, parts.dwUrlPathLength);
    if (endpoint.path.empty()) endpoint.path = L"/";
    if (endpoint.path != L"/" && endpoint.path != L"/connect") return false;
    if (parts.dwExtraInfoLength != 0) return false;
    endpoint.secure = _wcsicmp(scheme.c_str(), L"https") == 0;
    const bool insecure = _wcsicmp(scheme.c_str(), L"http") == 0;
    if (!endpoint.secure && !(insecure && IsLoopbackHost(endpoint.host))) return false;
    endpoint.port = parts.nPort;
    endpoint.path = L"/connect";
    return true;
}

bool SendText(HINTERNET socket, const std::string& text) {
    if (text.size() > kMaxMessageBytes) return false;
    return WinHttpWebSocketSend(socket, WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE,
        const_cast<char*>(text.data()), static_cast<DWORD>(text.size())) == NO_ERROR;
}

bool ReceiveText(HINTERNET socket, std::string& output) {
    output.clear();
    char buffer[4096];
    while (output.size() <= kMaxMessageBytes) {
        DWORD bytes = 0;
        WINHTTP_WEB_SOCKET_BUFFER_TYPE type{};
        const DWORD result = WinHttpWebSocketReceive(socket, buffer, sizeof(buffer), &bytes, &type);
        if (result != NO_ERROR || type == WINHTTP_WEB_SOCKET_CLOSE_BUFFER_TYPE ||
            type == WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE ||
            type == WINHTTP_WEB_SOCKET_BINARY_FRAGMENT_BUFFER_TYPE) return false;
        if (output.size() + bytes > kMaxMessageBytes) return false;
        output.append(buffer, bytes);
        if (type == WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE) return true;
        if (type != WINHTTP_WEB_SOCKET_UTF8_FRAGMENT_BUFFER_TYPE) return false;
    }
    return false;
}

void CloseHandles(HINTERNET& socket, HINTERNET& request, HINTERNET& connection, HINTERNET& session) {
    if (socket) {
        WinHttpWebSocketClose(socket, WINHTTP_WEB_SOCKET_SUCCESS_CLOSE_STATUS, nullptr, 0);
        WinHttpCloseHandle(socket); socket = nullptr;
    }
    if (request) { WinHttpCloseHandle(request); request = nullptr; }
    if (connection) { WinHttpCloseHandle(connection); connection = nullptr; }
    if (session) { WinHttpCloseHandle(session); session = nullptr; }
}

void RunTransport(std::string endpointText, std::string credential,
                  std::string adapterVersion, std::string gameBuild, std::string ue4ssVersion) {
    HINTERNET session = nullptr, connection = nullptr, request = nullptr, socket = nullptr;
    auto wipeCredential = [&]() {
        if (!credential.empty()) SecureZeroMemory(credential.data(), credential.size());
        credential.clear();
    };

    try {
        Endpoint endpoint;
        if (!ParseEndpoint(endpointText, endpoint)) {
            wipeCredential();
            throw std::runtime_error("Endpoint must be HTTPS; HTTP is allowed only for loopback.");
        }
        session = WinHttpOpen(L"HogwartsRP-UE4SS/0.1", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
            WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
        if (!session) throw std::runtime_error("WinHTTP session could not be opened.");
        WinHttpSetTimeouts(session, 5000, 5000, 5000, 10000);
        connection = WinHttpConnect(session, endpoint.host.c_str(), endpoint.port, 0);
        if (!connection) throw std::runtime_error("Server connection could not be opened.");
        request = WinHttpOpenRequest(connection, L"GET", endpoint.path.c_str(), nullptr,
            WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, endpoint.secure ? WINHTTP_FLAG_SECURE : 0);
        if (!request) throw std::runtime_error("WebSocket request could not be created.");
        if (!WinHttpSetOption(request, WINHTTP_OPTION_UPGRADE_TO_WEB_SOCKET, nullptr, 0))
            throw std::runtime_error("WinHTTP WebSocket upgrade could not be enabled.");
        if (!WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
            WINHTTP_NO_REQUEST_DATA, 0, 0, 0) || !WinHttpReceiveResponse(request, nullptr))
            throw std::runtime_error("WebSocket handshake request failed.");
        DWORD statusCode = 0, statusSize = sizeof(statusCode);
        if (!WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
            WINHTTP_HEADER_NAME_BY_INDEX, &statusCode, &statusSize, WINHTTP_NO_HEADER_INDEX) || statusCode != 101)
            throw std::runtime_error("Server did not accept the WebSocket upgrade.");
        socket = WinHttpWebSocketCompleteUpgrade(request, 0);
        if (!socket) throw std::runtime_error("WebSocket upgrade could not be completed.");
        WinHttpCloseHandle(request); request = nullptr;

        SetStatus("authenticating", "WebSocket connected; authenticating one-use join ticket.");
        const std::string auth = "{\"type\":\"authenticate\",\"credential\":\"" + JsonEscape(credential) + "\"}";
        wipeCredential();
        if (!SendText(socket, auth)) throw std::runtime_error("Authentication frame could not be sent.");
        std::string response;
        if (!ReceiveText(socket, response) || !HasType(response, "connected"))
            throw std::runtime_error("Server rejected the join ticket or returned an invalid session response.");

        const std::string hello =
            "{\"type\":\"game_adapter_hello\",\"protocolVersion\":1,\"adapterVersion\":\"" +
            JsonEscape(adapterVersion) + "\",\"gameBuild\":" + JsonOptional(gameBuild) +
            ",\"ue4ssVersion\":" + JsonOptional(ue4ssVersion) +
            ",\"capabilities\":[\"local_position_read\",\"camera_rotation_read\"]}";
        if (!SendText(socket, hello) || !ReceiveText(socket, response) ||
            !HasType(response, "game_adapter_ready"))
            throw std::runtime_error("Server rejected the game-adapter handshake.");
        SetStatus("connected", "Authenticated game-adapter session established; heartbeat active.");

        std::int64_t sequence = 0;
        while (!g_state.stopping.load()) {
            const auto deadline = std::chrono::steady_clock::now() + kHeartbeatInterval;
            while (!g_state.stopping.load() && std::chrono::steady_clock::now() < deadline)
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            if (g_state.stopping.load()) break;
            const std::string heartbeat = "{\"type\":\"game_adapter_heartbeat\",\"sequence\":" +
                std::to_string(++sequence) + "}";
            if (!SendText(socket, heartbeat) || !ReceiveText(socket, response) ||
                !HasType(response, "game_adapter_heartbeat_ack") || !HasSequence(response, sequence))
                throw std::runtime_error("Heartbeat failed or was rejected by the server.");
        }
        SetStatus("disconnected", "Transport stopped.");
    }
    catch (const std::exception& exception) {
        wipeCredential();
        SetStatus("error", exception.what());
    }

    CloseHandles(socket, request, connection, session);
    {
        std::lock_guard lock(g_state.mutex);
        g_state.running = false;
    }
}

int LuaConnect(lua_State* lua) {
    const char* endpoint = luaL_checkstring(lua, 1);
    const char* credential = luaL_checkstring(lua, 2);
    const char* adapter = luaL_optstring(lua, 3, "0.1.0");
    const char* game = luaL_optstring(lua, 4, "");
    const char* ue4ss = luaL_optstring(lua, 5, "");
    if (!*credential || std::strlen(credential) > 4096) return luaL_error(lua, "invalid join credential");
    if (std::strlen(endpoint) > 2048 || std::strlen(adapter) > 32 ||
        std::strlen(game) > 64 || std::strlen(ue4ss) > 64)
        return luaL_error(lua, "transport argument exceeds its size limit");

    std::string endpointCopy(endpoint);
    std::string credentialCopy(credential);
    std::string adapterCopy(adapter);
    std::string gameCopy(game);
    std::string ue4ssCopy(ue4ss);
    std::lock_guard lock(g_state.mutex);
    if (g_state.running) return luaL_error(lua, "transport is already running");
    if (g_state.worker.joinable()) g_state.worker.join();
    g_state.stopping = false;
    g_state.running = true;
    g_state.status = "starting";
    g_state.detail = "Starting authenticated WebSocket transport.";
    g_state.worker = std::thread(RunTransport, std::move(endpointCopy), std::move(credentialCopy),
        std::move(adapterCopy), std::move(gameCopy), std::move(ue4ssCopy));
    lua_pushboolean(lua, 1);
    return 1;
}

int LuaDisconnect(lua_State* lua) {
    (void)lua;
    g_state.stopping = true;
    if (g_state.worker.joinable()) g_state.worker.join();
    lua_pushboolean(lua, 1);
    return 1;
}

int LuaStatus(lua_State* lua) {
    std::lock_guard lock(g_state.mutex);
    lua_createtable(lua, 0, 3);
    lua_pushlstring(lua, g_state.status.data(), g_state.status.size());
    lua_setfield(lua, -2, "state");
    lua_pushlstring(lua, g_state.detail.data(), g_state.detail.size());
    lua_setfield(lua, -2, "detail");
    lua_pushboolean(lua, g_state.running);
    lua_setfield(lua, -2, "running");
    return 1;
}

const luaL_Reg kFunctions[] = {
    {"connect", LuaConnect},
    {"disconnect", LuaDisconnect},
    {"status", LuaStatus},
    {nullptr, nullptr}
};
}

extern "C" __declspec(dllexport) int luaopen_hogwartsrp_transport(lua_State* lua) {
    luaL_newlib(lua, kFunctions);
    return 1;
}
