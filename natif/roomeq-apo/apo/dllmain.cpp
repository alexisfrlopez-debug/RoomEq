// RoomEq APO — points d'entrée DLL Windows (spec I-AUD-09, N-APO-01).
// Toute la logique vit dans RoomEqApo.cpp (portable, testée sous Linux) :
// ce fichier ne fait que coller à la ABI de chargement COM de Windows.
#include "RoomEqApo.h"

#if defined(_WIN32)

// kernel32, déclaré à la main (pas de <windows.h> dans ce module — cf.
// apo_abi.h : le code est compilé à l'identique sous Linux pour les tests).
extern "C" __declspec(dllimport) int __stdcall DisableThreadLibraryCalls(void* hModule);

// DllMain : rien d'autre — les threads ne démarrent JAMAIS ici (verrou
// chargeur) : le surveillant de configuration démarre au LockForProcess.
extern "C" int __stdcall DllMain(void* hModule, unsigned long reason, void* /*reserved*/)
{
    if (reason == 1 /* DLL_PROCESS_ATTACH */)
        DisableThreadLibraryCalls(hModule);
    return 1;
}

extern "C" HRESULT __stdcall DllGetClassObject(const GUID& rclsid, const GUID& riid, void** ppv)
{
    return roomeq::apo::RoomeqDllGetClassObject(rclsid, riid, ppv);
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return roomeq::apo::RoomeqDllCanUnloadNow();
}

#endif // _WIN32
