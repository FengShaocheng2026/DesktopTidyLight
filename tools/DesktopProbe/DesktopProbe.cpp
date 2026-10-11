#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <exdisp.h>
#include <servprov.h>
#include <shlwapi.h>
#include <wrl/client.h>
#include <cstdio>
#include <cwchar>

using Microsoft::WRL::ComPtr;

static bool Check(HRESULT result, const wchar_t* step) {
    if (SUCCEEDED(result)) return true;
    std::fwprintf(stderr, L"%ls failed: 0x%08lX\n", step, static_cast<unsigned long>(result));
    return false;
}

static HRESULT GetDesktopView(ComPtr<IFolderView>& view) {
    ComPtr<IShellWindows> windows;
    HRESULT hr = CoCreateInstance(CLSID_ShellWindows, nullptr, CLSCTX_LOCAL_SERVER,
                                  IID_PPV_ARGS(&windows));
    if (FAILED(hr)) return hr;

    VARIANT location;
    VariantInit(&location);
    location.vt = VT_I4;
    location.lVal = CSIDL_DESKTOP;
    VARIANT empty;
    VariantInit(&empty);
    long hwnd = 0;
    ComPtr<IDispatch> dispatch;
    hr = windows->FindWindowSW(&location, &empty, SWC_DESKTOP, &hwnd,
                               SWFO_NEEDDISPATCH, &dispatch);
    if (FAILED(hr)) return hr;

    ComPtr<IServiceProvider> services;
    hr = dispatch.As(&services);
    if (FAILED(hr)) return hr;
    ComPtr<IShellBrowser> browser;
    hr = services->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&browser));
    if (FAILED(hr)) return hr;
    ComPtr<IShellView> shellView;
    hr = browser->QueryActiveShellView(&shellView);
    if (FAILED(hr)) return hr;
    return shellView.As(&view);
}

int wmain(int argc, wchar_t** argv) {
    const bool list = argc == 2 && std::wcscmp(argv[1], L"--list") == 0;
    const bool selected = argc == 2 && std::wcscmp(argv[1], L"--selected") == 0;
    if (argc > 1 && !list && !selected) {
        std::fwprintf(stderr, L"Usage: DesktopProbe.exe [--list|--selected]\n");
        return 2;
    }

    const HRESULT init = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (!Check(init, L"CoInitializeEx")) return 1;
    struct Uninitialize {
        ~Uninitialize() { CoUninitialize(); }
    } uninitialize;

    ComPtr<IFolderView> view;
    if (!Check(GetDesktopView(view), L"GetDesktopView")) return 1;
    if (selected) {
        int selectedCount = 0;
        if (!Check(view->ItemCount(SVGIO_SELECTION, &selectedCount), L"ItemCount")) return 1;
        if (selectedCount == 0) {
            std::wprintf(L"Selected items: 0, position errors: 0\n");
            return 0;
        }
    }
    ComPtr<IShellFolder> folder;
    if (!Check(view->GetFolder(IID_PPV_ARGS(&folder)), L"GetFolder")) return 1;
    ComPtr<IEnumIDList> items;
    if (!Check(view->Items(selected ? SVGIO_SELECTION : SVGIO_ALLVIEW,
                           IID_PPV_ARGS(&items)), L"Items")) return 1;

    unsigned count = 0;
    unsigned positionErrors = 0;
    unsigned nameErrors = 0;
    HRESULT next = S_OK;
    PITEMID_CHILD id = nullptr;
    while ((next = items->Next(1, &id, nullptr)) == S_OK) {
        POINT position{};
        const HRESULT positionResult = view->GetItemPosition(id, &position);
        if (FAILED(positionResult)) ++positionErrors;

        if (list) {
            STRRET display{};
            PWSTR name = nullptr;
            const HRESULT nameResult = folder->GetDisplayNameOf(id, SHGDN_NORMAL, &display);
            if (SUCCEEDED(nameResult) && SUCCEEDED(StrRetToStrW(&display, id, &name))) {
                std::wprintf(L"%ld,%ld\t%ls\n", position.x, position.y, name);
            } else {
                ++nameErrors;
            }
            CoTaskMemFree(name);
        }
        CoTaskMemFree(id);
        id = nullptr;
        ++count;
    }
    if (!Check(next, L"Enumeration")) return 1;

    const HRESULT autoArrange = view->GetAutoArrange();
    if (!Check(autoArrange, L"GetAutoArrange")) return 1;
    std::wprintf(L"%ls: %u, position errors: %u, name errors: %u, auto arrange: %ls\n",
                 selected ? L"Selected items" : L"Items",
                 count, positionErrors, nameErrors,
                 autoArrange == S_OK ? L"on" : L"off");
    return positionErrors == 0 ? 0 : 1;
}
