#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <exdisp.h>
#include <servprov.h>
#include <shlwapi.h>
#include <wrl/client.h>
#include <cstdio>
#include <cwchar>
#include <string>

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

static PITEMID_CHILD FindFile(IFolderView* view, IShellFolder* folder,
                              const wchar_t* path) {
    ComPtr<IEnumIDList> items;
    if (FAILED(view->Items(SVGIO_ALLVIEW, IID_PPV_ARGS(&items)))) return nullptr;
    PITEMID_CHILD id = nullptr;
    while (items->Next(1, &id, nullptr) == S_OK) {
        STRRET display{};
        PWSTR parsingName = nullptr;
        const bool found = SUCCEEDED(folder->GetDisplayNameOf(id, SHGDN_FORPARSING, &display)) &&
                           SUCCEEDED(StrRetToStrW(&display, id, &parsingName)) &&
                           _wcsicmp(parsingName, path) == 0;
        CoTaskMemFree(parsingName);
        if (found) return id;
        CoTaskMemFree(id);
        id = nullptr;
    }
    return nullptr;
}

static int TestPosition(IFolderView* view, IShellFolder* folder, bool manualMode) {
    struct RestoreMode {
        ComPtr<IFolderView2> flagsView;
        ComPtr<IFolderViewOptions> options;
        DWORD oldFlags = 0;
        FOLDERVIEWOPTIONS oldOptions = FVO_DEFAULT;
        bool flagsChanged = false;
        bool optionsChanged = false;
        ~RestoreMode() {
            if (flagsChanged && FAILED(flagsView->SetCurrentFolderFlags(
                    FWF_AUTOARRANGE | FWF_SNAPTOGRID, oldFlags)))
                std::fwprintf(stderr, L"Restore desktop arrangement flags failed.\n");
            if (optionsChanged && FAILED(options->SetFolderViewOptions(
                    FVO_CUSTOMPOSITION, oldOptions)))
                std::fwprintf(stderr, L"Restore desktop view options failed.\n");
        }
    } mode;
    if (manualMode) {
        if (!Check(view->QueryInterface(IID_PPV_ARGS(&mode.flagsView)),
                   L"Get IFolderView2") ||
            !Check(view->QueryInterface(IID_PPV_ARGS(&mode.options)),
                   L"Get IFolderViewOptions") ||
            !Check(mode.flagsView->GetCurrentFolderFlags(&mode.oldFlags),
                   L"GetCurrentFolderFlags") ||
            !Check(mode.options->GetFolderViewOptions(&mode.oldOptions),
                   L"GetFolderViewOptions")) return 1;
        std::wprintf(L"Before test: auto arrange %ls, snap to grid %ls\n",
                     (mode.oldFlags & FWF_AUTOARRANGE) ? L"on" : L"off",
                     (mode.oldFlags & FWF_SNAPTOGRID) ? L"on" : L"off");
        const HRESULT optionResult = mode.options->SetFolderViewOptions(
            FVO_CUSTOMPOSITION, FVO_CUSTOMPOSITION);
        if (optionResult == E_NOTIMPL) {
            std::wprintf(L"Custom position option is not implemented by this desktop view.\n");
        } else if (!Check(optionResult, L"SetFolderViewOptions")) {
            return 1;
        } else {
            mode.optionsChanged = true;
        }
        if (!Check(mode.flagsView->SetCurrentFolderFlags(
                       FWF_AUTOARRANGE | FWF_SNAPTOGRID, 0),
                   L"Disable auto arrange and snap to grid")) return 1;
        mode.flagsChanged = true;
        if (view->GetAutoArrange() != S_FALSE) {
            std::fwprintf(stderr, L"Auto arrange is still on; test stopped.\n");
            return 1;
        }
    }
    PWSTR desktop = nullptr;
    if (!Check(SHGetKnownFolderPath(FOLDERID_Desktop, 0, nullptr, &desktop),
               L"SHGetKnownFolderPath")) return 1;
    GUID guid{};
    if (!Check(CoCreateGuid(&guid), L"CoCreateGuid")) {
        CoTaskMemFree(desktop);
        return 1;
    }
    wchar_t guidText[40]{};
    StringFromGUID2(guid, guidText, 40);
    const std::wstring path = std::wstring(desktop) + L"\\DesktopTidyLight-P1-" +
                              guidText + L".txt";
    CoTaskMemFree(desktop);

    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ,
                              nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        std::fwprintf(stderr, L"Create test file failed: %lu\n", GetLastError());
        return 1;
    }
    const char marker[] = "DesktopTidyLight temporary position test\r\n";
    DWORD written = 0;
    const bool wrote = WriteFile(file, marker, sizeof(marker) - 1, &written, nullptr) &&
                       written == sizeof(marker) - 1 && FlushFileBuffers(file);
    CloseHandle(file);
    struct DeleteTestFile {
        const std::wstring& path;
        ~DeleteTestFile() {
            if (!DeleteFileW(path.c_str())) {
                std::fwprintf(stderr, L"Remove test file failed: %lu\n", GetLastError());
            } else {
                SHChangeNotify(SHCNE_DELETE, SHCNF_PATHW, path.c_str(), nullptr);
            }
        }
    } cleanup{path};
    if (!wrote) return 1;
    SHChangeNotify(SHCNE_CREATE, SHCNF_PATHW, path.c_str(), nullptr);

    PITEMID_CHILD id = nullptr;
    for (int attempt = 0; attempt < 20 && !id; ++attempt) {
        id = FindFile(view, folder, path.c_str());
        if (!id) Sleep(250);
    }
    if (!id) {
        std::fwprintf(stderr, L"Explorer did not show the test icon.\n");
        return 1;
    }
    POINT original{};
    if (!Check(view->GetItemPosition(id, &original), L"GetItemPosition")) {
        CoTaskMemFree(id);
        return 1;
    }
    const HRESULT autoArrange = view->GetAutoArrange();
    POINT requested{original.x + 24, original.y + 24};
    PCITEMID_CHILD ids[1]{id};
    const HRESULT moved = view->SelectAndPositionItems(1, ids, &requested,
                                                       SVSI_POSITIONITEM);
    Sleep(250);
    POINT after{};
    const HRESULT readAfter = view->GetItemPosition(id, &after);
    const HRESULT restored = view->SelectAndPositionItems(1, ids, &original,
                                                          SVSI_POSITIONITEM);
    Sleep(250);
    POINT finalPosition{};
    const HRESULT readFinal = view->GetItemPosition(id, &finalPosition);
    CoTaskMemFree(id);

    std::wprintf(L"Test icon: original (%ld,%ld), requested (%ld,%ld), after (%ld,%ld), restored (%ld,%ld)\n",
                 original.x, original.y, requested.x, requested.y,
                 after.x, after.y, finalPosition.x, finalPosition.y);
    std::wprintf(L"Auto arrange: %ls\n", autoArrange == S_OK ? L"on" :
                 autoArrange == S_FALSE ? L"off" : L"unknown");
    std::wprintf(L"Move: 0x%08lX, read: 0x%08lX, restore: 0x%08lX, final read: 0x%08lX\n",
                 static_cast<unsigned long>(moved), static_cast<unsigned long>(readAfter),
                 static_cast<unsigned long>(restored), static_cast<unsigned long>(readFinal));
    if (FAILED(moved) || FAILED(readAfter) || FAILED(restored) || FAILED(readFinal)) return 1;
    if (after.x != requested.x || after.y != requested.y) return 2;
    if (finalPosition.x != original.x || finalPosition.y != original.y) return 3;
    return 0;
}

int wmain(int argc, wchar_t** argv) {
    const bool list = argc == 2 && std::wcscmp(argv[1], L"--list") == 0;
    const bool selected = argc == 2 && std::wcscmp(argv[1], L"--selected") == 0;
    const bool testPosition = argc == 2 && std::wcscmp(argv[1], L"--test-position") == 0;
    const bool manualMode = argc == 2 && std::wcscmp(argv[1], L"--test-position-manual") == 0;
    if (argc > 1 && !list && !selected && !testPosition && !manualMode) {
        std::fwprintf(stderr, L"Usage: DesktopProbe.exe [--list|--selected|--test-position|--test-position-manual]\n");
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
    if (testPosition || manualMode) {
        const int result = TestPosition(view.Get(), folder.Get(), manualMode);
        const HRESULT current = view->GetAutoArrange();
        std::wprintf(L"Auto arrange after test: %ls\n",
                     current == S_OK ? L"on" : current == S_FALSE ? L"off" : L"unknown");
        ComPtr<IFolderView2> flagsView;
        DWORD flags = 0;
        if (manualMode && SUCCEEDED(view.As(&flagsView)) &&
            SUCCEEDED(flagsView->GetCurrentFolderFlags(&flags)))
            std::wprintf(L"Snap to grid after test: %ls\n",
                         (flags & FWF_SNAPTOGRID) ? L"on" : L"off");
        return result;
    }
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
