// RoomEq APO — coutures Windows (spec I-AUD-09, N-APO-01).
// Le cœur (RoomEqApo.cpp, apo_config.cpp) compile à l'identique sous Linux
// pour le harnais de tests : tout ce qui touche aux API Win32 vit ici.
#include "apo_abi.h"
#include "apo_config.h"

#include <cstddef>

#if defined(_WIN32)

extern "C" {
__declspec(dllimport) void* __stdcall CoTaskMemAlloc(std::size_t cb);
__declspec(dllimport) void __stdcall CoTaskMemFree(void* pv);
}

extern "C" void* WINAPI RoomeqCoTaskMemAlloc(std::size_t cb)
{
    return CoTaskMemAlloc(cb);
}

extern "C" void WINAPI RoomeqCoTaskMemFree(void* pv)
{
    CoTaskMemFree(pv);
}

// ------------------------------------------------- horodatage fin (Windows)
// GetFileAttributesExA : mtime en unités de 100 ns — la granularité SECONDE
// de _stat raterait les réécritures rapprochées d'active.json (l'application
// réécrit plusieurs fois par seconde pendant un glissement de courbe).
// Structures déclarées à la main (pas de <windows.h> dans le module, cf.
// apo_abi.h) — layouts fixes de winbase.h, inchangés depuis toujours.

namespace roomeq::apo {

struct RoomeqFileTime {
    unsigned long dwLowDateTime;
    unsigned long dwHighDateTime;
};

struct RoomeqFileAttributeData {
    unsigned long dwFileAttributes;
    RoomeqFileTime ftCreationTime;
    RoomeqFileTime ftLastAccessTime;
    RoomeqFileTime ftLastWriteTime;
    unsigned long nFileSizeHigh;
    unsigned long nFileSizeLow;
};

extern "C" __declspec(dllimport) int __stdcall GetFileAttributesExA(
    const char* lpFileName, int fInfoLevelId, void* lpFileInformation);

bool RoomeqFileStamp(const char* path, long long* fineTime, long long* size)
{
    RoomeqFileAttributeData d;
    if (GetFileAttributesExA(path, 0 /* GetFileExInfoStandard */, &d) == 0)
        return false;
    *fineTime = (static_cast<long long>(d.ftLastWriteTime.dwHighDateTime) << 32)
              | static_cast<long long>(d.ftLastWriteTime.dwLowDateTime);
    *size = (static_cast<long long>(d.nFileSizeHigh) << 32)
          | static_cast<long long>(d.nFileSizeLow);
    return true;
}

} // namespace roomeq::apo

#endif // _WIN32
