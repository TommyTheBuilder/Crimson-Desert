#include <Windows.h>
#include <atomic>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <string>
#include "game/inventory.h"
#include "core/logger.h"

namespace trinity::bridge
{
    namespace
    {
        constexpr wchar_t kPipe[] = L"\\\\.\\pipe\\PywelTrainer-CrimsonDesert";
        std::atomic<bool> g_stop{false};
        HANDLE g_thread = nullptr;

        std::string Err(const char* text) { return std::string("ERR\t") + text + "\n"; }

        bool FindType(const std::string& key, uint16_t* out)
        {
            if (!game::Inventory::CatalogReady()) return false;
            const int cats = game::Inventory::CatalogCategoryCount();
            for (int c = 0; c < cats; ++c)
            {
                const int n = game::Inventory::CatalogItemCount(c);
                for (int i = 0; i < n; ++i)
                {
                    game::Inventory::ItemInfo info{};
                    if (!game::Inventory::GetCatalogItem(c, i, &info) || !info.key) continue;
                    if (_stricmp(info.key, key.c_str()) == 0)
                    {
                        *out = info.typeId;
                        return true;
                    }
                }
            }
            return false;
        }

        std::string Status()
        {
            const bool inventory = game::Inventory::Ready();
            const bool catalog = game::Inventory::CatalogReady();
            const bool persist = game::Inventory::EditsPersist();
            char b[128];
            _snprintf_s(b, sizeof(b), _TRUNCATE, "OK\tSTATUS\tinventory=%d\tcatalog=%d\tpersist=%d\n",
                        inventory ? 1 : 0, catalog ? 1 : 0, persist ? 1 : 0);
            return b;
        }

        std::string Add(const std::string& line)
        {
            const size_t a = line.find('\t');
            const size_t b = a == std::string::npos ? std::string::npos : line.find('\t', a + 1);
            if (a == std::string::npos || b == std::string::npos) return Err("Ungültiger ADD-Befehl.");
            const std::string key = line.substr(a + 1, b - a - 1);
            const std::string q = line.substr(b + 1);
            if (key.empty() || key.size() > 512) return Err("Ungültiger Gegenstandsschlüssel.");
            char* end = nullptr;
            const long long qty = _strtoi64(q.c_str(), &end, 10);
            if (!end || *end || qty < 1 || qty > 999999) return Err("Menge muss zwischen 1 und 999999 liegen.");
            if (!game::Inventory::Ready()) return Err("Inventar ist noch nicht bereit. Spielstand vollständig laden.");
            if (!game::Inventory::EditsPersist()) return Err("Server-Inventar ist noch nicht bereit. Kurz warten oder Spielstand neu laden.");
            uint16_t typeId = 0;
            if (!FindType(key, &typeId)) return Err("Gegenstand wurde in der laufenden Spielversion nicht eindeutig gefunden.");
            if (!game::Inventory::AddItem(typeId, static_cast<int64_t>(qty))) return Err("Live-Hinzufügen ist gerade beschäftigt oder der Gegenstand wird vom Spiel abgelehnt.");

            const ULONGLONG until = GetTickCount64() + 8000;
            while (GetTickCount64() < until)
            {
                const auto s = game::Inventory::AddStatus();
                if (s == game::Inventory::AddState::Added)
                {
                    game::Inventory::ForceRefresh();
                    char out[80];
                    _snprintf_s(out, sizeof(out), _TRUNCATE, "OK\tADDED\t%u\n", static_cast<unsigned>(typeId));
                    return out;
                }
                if (s == game::Inventory::AddState::Failed) return Err("Die Spiel-Engine hat das Hinzufügen abgelehnt. Prüfe freien Inventarplatz und Spielversion.");
                Sleep(10);
            }
            return Err("Live-Hinzufügen hat nicht rechtzeitig auf dem Spielthread abgeschlossen. Die Live-Signaturen passen möglicherweise nicht zu dieser Spielversion.");
        }

        std::string Handle(std::string line)
        {
            while (!line.empty() && (line.back() == '\r' || line.back() == '\n')) line.pop_back();
            if (line == "PING") return "OK\tPONG\n";
            if (line == "STATUS") return Status();
            if (line.rfind("ADD\t", 0) == 0) return Add(line);
            return Err("Unbekannter Live-Befehl.");
        }

        DWORD WINAPI ThreadProc(void*)
        {
            while (!g_stop.load(std::memory_order_acquire))
            {
                HANDLE pipe = CreateNamedPipeW(kPipe, PIPE_ACCESS_DUPLEX,
                    PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                    1, 4096, 4096, 1000, nullptr);
                if (pipe == INVALID_HANDLE_VALUE) { Sleep(250); continue; }
                BOOL connected = ConnectNamedPipe(pipe, nullptr) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);
                if (connected && !g_stop.load(std::memory_order_acquire))
                {
                    std::string line;
                    char buf[512];
                    for (;;) {
                        DWORD got = 0;
                        if (!ReadFile(pipe, buf, sizeof(buf), &got, nullptr) || got == 0) break;
                        line.append(buf, buf + got);
                        const size_t nl = line.find('\n');
                        if (nl != std::string::npos) { line.resize(nl); break; }
                        if (line.size() > 4096) { line = ""; break; }
                    }
                    const std::string response = line.empty() ? Err("Leerer Live-Befehl.") : Handle(line);
                    DWORD sent = 0;
                    WriteFile(pipe, response.data(), static_cast<DWORD>(response.size()), &sent, nullptr);
                    FlushFileBuffers(pipe);
                }
                DisconnectNamedPipe(pipe);
                CloseHandle(pipe);
            }
            return 0;
        }
    }

    bool Start()
    {
        if (g_thread) return true;
        g_stop.store(false, std::memory_order_release);
        g_thread = CreateThread(nullptr, 0, &ThreadProc, nullptr, 0, nullptr);
        if (!g_thread) { LOG_WARN("pywel bridge: named-pipe thread could not start"); return false; }
        return true;
    }

    void Stop()
    {
        if (!g_thread) return;
        g_stop.store(true, std::memory_order_release);
        HANDLE h = CreateFileW(kPipe, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (h != INVALID_HANDLE_VALUE) CloseHandle(h);
        WaitForSingleObject(g_thread, 1500);
        CloseHandle(g_thread);
        g_thread = nullptr;
    }
}