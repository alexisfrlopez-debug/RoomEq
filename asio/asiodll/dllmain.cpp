/* -*- coding: utf-8 -*-
   RoomEq ASIO bridge — COM plumbing (DLL entry point, class factory,
   registration). The ASIO driver itself is RoomEqAsioDriver.
*/
#include <windows.h>
#include <objbase.h>
#include <olectl.h>   /* SELFREG_E_CLASS (DllRegisterServer) */
#include <new>        /* std::nothrow */
#include <stdio.h>
#include <string.h>

#include "RoomEqAsio.h"

static HMODULE g_module = NULL;

/* {8F5E3A91-4B2C-4D6E-9A1F-2E7C5B8D9A03} */
static const CLSID kClsid =
{ 0x8f5e3a91, 0x4b2c, 0x4d6e, { 0x9a, 0x1f, 0x2e, 0x7c, 0x5b, 0x8d, 0x9a, 0x03 } };

/* ---- IClassFactory ---- */

class RoomEqAsioFactory : public IClassFactory
{
public:
    ULONG STDMETHODCALLTYPE AddRef() override { return 2; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override
    {
        if (ppv == NULL) return E_POINTER;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
        {
            *ppv = static_cast<IClassFactory*>(this);
            return S_OK;
        }
        *ppv = NULL;
        return E_NOINTERFACE;
    }

    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        if (ppv == NULL) return E_POINTER;
        if (outer != NULL) return CLASS_E_NOAGGREGATION;

        RoomEqAsioDriver* drv = new (std::nothrow) RoomEqAsioDriver();
        if (drv == NULL) return E_OUTOFMEMORY;

        HRESULT hr = drv->QueryInterface(riid, ppv);
        drv->Release();
        return hr;
    }

    HRESULT STDMETHODCALLTYPE LockServer(BOOL /*lock*/) override
    {
        return S_OK;
    }
};

static RoomEqAsioFactory s_factory;

/* ---- exports ---- */

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (ppv == NULL) return E_POINTER;
    if (!IsEqualCLSID(rclsid, kClsid)) return CLASS_E_CLASSNOTAVAILABLE;
    return s_factory.QueryInterface(riid, ppv);
}

STDAPI DllCanUnloadNow()
{
    return S_FALSE;   /* reste chargé : boucle de vie simple et fiable */
}

static LSTATUS regWrite(HKEY root, const wchar_t* subkey, const wchar_t* valueName,
                        const wchar_t* data, REGSAM access)
{
    HKEY k;
    LSTATUS r = RegCreateKeyExW(root, subkey, 0, NULL, 0,
                                KEY_WRITE | access, NULL, &k, NULL);
    if (r != ERROR_SUCCESS) return r;
    r = RegSetValueExW(k, valueName, 0, REG_SZ, (const BYTE*)data,
                       (DWORD)((wcslen(data) + 1) * sizeof(wchar_t)));
    RegCloseKey(k);
    return r;
}

static void setStringForModulePath(wchar_t* buf, size_t cch, const wchar_t* suffix)
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(g_module, path, MAX_PATH);
    _snwprintf_s(buf, cch, _TRUNCATE, L"%s%s", path, suffix);
}

STDAPI DllRegisterServer()
{
    wchar_t inproc[MAX_PATH + 16];
    setStringForModulePath(inproc, MAX_PATH + 16, L"");

    /* 1) HKLM d'abord (ASIO standard), repli HKCU (sans admin). */
    HKEY roots[2] = { HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER };
    int done = 0;
    for (int i = 0; i < 2; i++)
    {
        REGSAM access = (roots[i] == HKEY_LOCAL_MACHINE) ? KEY_WOW64_64KEY : 0;
        wchar_t key[128];
        /* HKLM\SOFTWARE\ASIO\RoomEq ASIO */
        if (regWrite(roots[i], L"SOFTWARE\\ASIO\\RoomEq ASIO", L"CLSID",
                     L"{8F5E3A91-4B2C-4D6E-9A1F-2E7C5B8D9A03}", access) == ERROR_SUCCESS)
        {
            regWrite(roots[i], L"SOFTWARE\\ASIO\\RoomEq ASIO", L"Description",
                     L"RoomEq ASIO (pont vers l'égaliseur RoomEq)", access);
            done = 1;
        }

        /* HKCR\CLSID\{...}\InprocServer32 */
        wcscpy_s(key, 128, L"Software\\CLASSES\\CLSID\\{8F5E3A91-4B2C-4D6E-9A1F-2E7C5B8D9A03}");
        if (regWrite(roots[i], key, NULL, L"RoomEq ASIO", access) == ERROR_SUCCESS)
        {
            regWrite(roots[i], key, NULL, L"RoomEq ASIO", access);
            wchar_t inprocKey[160];
            wcscpy_s(inprocKey, 160, key);
            wcscat_s(inprocKey, 160, L"\\InprocServer32");
            regWrite(roots[i], inprocKey, NULL, inproc, access);
            regWrite(roots[i], inprocKey, L"ThreadingModel", L"Apartment", access);
            done = 1;
        }
        if (done) break;
    }
    return done ? S_OK : SELFREG_E_CLASS;
}

STDAPI DllUnregisterServer()
{
    const wchar_t* asioKey = L"SOFTWARE\\ASIO\\RoomEq ASIO";
    const wchar_t* clsidKey = L"Software\\CLASSES\\CLSID\\{8F5E3A91-4B2C-4D6E-9A1F-2E7C5B8D9A03}";
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, asioKey);
    RegDeleteTreeW(HKEY_CURRENT_USER, asioKey);
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, clsidKey);
    RegDeleteTreeW(HKEY_CURRENT_USER, clsidKey);
    return S_OK;
}

/* ---- entry point ---- */

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID /*reserved*/)
{
    switch (reason)
    {
        case DLL_PROCESS_ATTACH:
            g_module = module;
            DisableThreadLibraryCalls(module);
            break;
        case DLL_PROCESS_DETACH:
        default:
            break;
    }
    return TRUE;
}
