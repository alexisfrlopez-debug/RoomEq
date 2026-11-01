// RoomEq VST — éditeur Win32 du greffon (spec I-AUD-08) : fenêtre enfant
// dessinée à la main (aucune ressource, aucune dépendance) reprenant les
// fonctions de la vue Cocoa du greffon AU : charger un preset (.json/.ffp)
// par bouton ou glisser-déposer, courbe de réponse 97 points, nom du preset,
// nombre de bandes, préampli et taux.
//
// Toutes les entrées sont défensives (échec de création → éditeur absent,
// le greffon reste utilisable via chunks/paramètres) ; le chargement d'un
// preset malformé affiche l'erreur sans jamais planter l'hôte.
#include "RoomEqVst.h"

#if defined(_WIN32) && !defined(ROOMEQ_VST_NO_EDITOR)

#include "roomeq/preset/preset.h"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

#include <windows.h>
#include <commdlg.h>
#include <shellapi.h>

namespace {

// ---------------------------------------------------------------- mesurettes

constexpr int kEditorWidth = 520;
constexpr int kEditorHeight = 240;
constexpr int kButtonId = 1;

/// Zone de tracé (coordonnées clientes).
constexpr int kPlotLeft = 10, kPlotRight = 510;
constexpr int kPlotTop = 34, kPlotBottom = 198;
/// Plage verticale affichée (dB) — bande de correction typique.
constexpr double kDbTop = 12.0, kDbBottom = -24.0;

struct EditorState {
    RoomEqVst* plugin = nullptr;
    HWND hwnd = nullptr;
    HWND button = nullptr;
    HFONT font = nullptr;
    HFONT bannerFont = nullptr; // bannière « aucun preset » (gras, orange)
};

std::wstring Utf8ToWide(const std::string& text)
{
    if (text.empty())
        return std::wstring();
    const int size = MultiByteToWideChar(CP_UTF8, 0, text.data(),
        static_cast<int>(text.size()), nullptr, 0);
    std::wstring out(static_cast<size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
        out.data(), size);
    return out;
}

/// Lit tout un fichier en octets (retourne false si inaccessible).
bool ReadFileBytes(const std::wstring& path, std::vector<uint8_t>& out)
{
    std::FILE* f = _wfopen(path.c_str(), L"rb");
    if (f == nullptr)
        return false;
    std::fseek(f, 0, SEEK_END);
    const long size = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    if (size <= 0 || size > (64 << 20)) { // garde-fou : 64 Mo maximum
        std::fclose(f);
        return false;
    }
    out.resize(static_cast<size_t>(size));
    const size_t read = std::fread(out.data(), 1, out.size(), f);
    std::fclose(f);
    return read == out.size();
}

/// Charge un preset dans le greffon puis rafraîchit la fenêtre.
/// Retourne false + message en cas d'échec (jamais d'exception sortante).
bool LoadPresetFromFile(EditorState& st, const std::wstring& path,
    std::string& error)
{
    try {
        std::vector<uint8_t> bytes;
        if (!ReadFileBytes(path, bytes)) {
            error = "Fichier illisible.";
            return false;
        }
        if (!st.plugin->ApplyPresetBytes(bytes.data(), bytes.size(), true, error))
            return false;
        InvalidateRect(st.hwnd, nullptr, TRUE);
        return true;
    } catch (const std::exception& e) {
        error = e.what();
        return false;
    } catch (...) {
        error = "Erreur inconnue.";
        return false;
    }
}

// ------------------------------------------------------------------ dessin

double FreqToX(double freq)
{
    const double logMin = std::log10(20.0), logMax = std::log10(20000.0);
    const double t = (std::log10(std::max(freq, 20.0)) - logMin)
                     / (logMax - logMin);
    return kPlotLeft + t * (kPlotRight - kPlotLeft);
}

double DbToY(double db)
{
    const double clamped = std::clamp(db, kDbBottom, kDbTop);
    const double t = (kDbTop - clamped) / (kDbTop - kDbBottom);
    return kPlotTop + t * (kPlotBottom - kPlotTop);
}

void DrawEditor(HDC hdc, EditorState& st)
{
    const RECT full = { 0, 0, kEditorWidth, kEditorHeight };
    HBRUSH background = CreateSolidBrush(RGB(20, 20, 30));
    FillRect(hdc, &full, background);
    DeleteObject(background);

    SelectObject(hdc, st.font);
    SetBkMode(hdc, TRANSPARENT);

    // --- grille : verticale 100 Hz / 1 kHz / 10 kHz, horizontale par 12 dB
    HPEN grid = CreatePen(PS_SOLID, 1, RGB(58, 58, 84));
    SelectObject(hdc, grid);
    for (double f : { 100.0, 1000.0, 10000.0 }) {
        const int x = static_cast<int>(FreqToX(f));
        MoveToEx(hdc, x, kPlotTop, nullptr);
        LineTo(hdc, x, kPlotBottom);
    }
    for (double db = kDbTop; db >= kDbBottom - 0.5; db -= 12.0) {
        const int y = static_cast<int>(DbToY(db));
        MoveToEx(hdc, kPlotLeft, y, nullptr);
        LineTo(hdc, kPlotRight, y);
    }

    // ligne 0 dB en avant-plan
    HPEN zero = CreatePen(PS_SOLID, 1, RGB(96, 96, 128));
    SelectObject(hdc, zero);
    const int y0 = static_cast<int>(DbToY(0.0));
    MoveToEx(hdc, kPlotLeft, y0, nullptr);
    LineTo(hdc, kPlotRight, y0);

    // repères de fréquence
    SetTextColor(hdc, RGB(150, 150, 170));
    TextOutW(hdc, kPlotLeft + 2, kPlotBottom + 3, L"20 Hz", 5);
    TextOutW(hdc, static_cast<int>(FreqToX(100.0)) - 10, kPlotBottom + 3,
        L"100", 3);
    TextOutW(hdc, static_cast<int>(FreqToX(1000.0)) - 5, kPlotBottom + 3,
        L"1k", 2);
    TextOutW(hdc, static_cast<int>(FreqToX(10000.0)) - 10, kPlotBottom + 3,
        L"10k", 3);
    wchar_t dbLabel[16];
    const int n0 = swprintf(dbLabel, 16, L"%.0f dB", 0.0);
    TextOutW(hdc, kPlotRight - 46, y0 - 16, dbLabel, n0);

    // --- preset courant (courbe, points de bandes et bandeau)
    const roomeq::preset::PresetDef preset = st.plugin->CurrentPreset();
    int bands = 0;
    double maxGainDb = 0.0;
    for (const auto& b : preset.bands)
        if (b.enabled) {
            ++bands;
            maxGainDb = std::max(maxGainDb, std::fabs(b.gainDb));
        }
    const double preampShift = static_cast<double>(st.plugin->PreampParam())
                               - preset.preampDb;

    // --- courbe composite (97 points, préampli du paramètre inclus)
    std::vector<double> curve;
    try {
        curve = st.plugin->ResponseCurve();
    } catch (...) {
        curve.clear();
    }
    if (curve.size() >= 4) {
        const size_t points = curve.size() / 2;
        std::vector<POINT> pts(points);
        for (size_t i = 0; i < points; ++i) {
            pts[i].x = static_cast<LONG>(FreqToX(curve[i * 2]));
            pts[i].y = static_cast<LONG>(DbToY(curve[i * 2 + 1]));
        }
        HPEN curvePen = CreatePen(PS_SOLID, 2, RGB(140, 122, 246));
        SelectObject(hdc, curvePen);
        Polyline(hdc, pts.data(), static_cast<int>(points));
        DeleteObject(curvePen);
    }

    // --- repères de bandes (v3.4.2) : un point par bande ACTIVE, posé sur
    // la courbe à la fréquence centrale — les bandes sont visibles même
    // quand leur gain est nul (elles tombent alors sur la ligne 0 dB, ce
    // qui rend un preset plat immédiatement lisible : « 10 bandes à 0 dB »
    // au lieu d'une courbe vide)
    if (bands > 0) {
        HPEN dotPen = CreatePen(PS_SOLID, 1, RGB(186, 174, 255));
        HBRUSH dotBrush = CreateSolidBrush(RGB(186, 174, 255));
        HPEN oldPen = static_cast<HPEN>(SelectObject(hdc, dotPen));
        HBRUSH oldBrush = static_cast<HBRUSH>(SelectObject(hdc, dotBrush));
        for (const auto& b : preset.bands) {
            if (!b.enabled)
                continue;
            const int x = static_cast<int>(FreqToX(b.frequency));
            const int y = static_cast<int>(DbToY(b.gainDb + preampShift));
            Ellipse(hdc, x - 3, y - 3, x + 3, y + 3);
        }
        SelectObject(hdc, oldPen);
        SelectObject(hdc, oldBrush);
        DeleteObject(dotPen);
        DeleteObject(dotBrush);
    }

    // --- bandeau d'information
    wchar_t line1[96];
    swprintf(line1, 96, L"Preset : %s",
        Utf8ToWide(preset.name).c_str());
    SetTextColor(hdc, RGB(235, 235, 245));
    TextOutW(hdc, 12, 8, line1, lstrlenW(line1));

    wchar_t line2[96];
    if (st.plugin->PresetHasEffect())
        swprintf(line2, 96, L"%d bandes · gain max %.1f dB · preamp %+.1f dB",
            bands, maxGainDb, st.plugin->PreampParam());
    else
        swprintf(line2, 96, L"%d bandes à 0 dB · preamp %+.1f dB",
            bands, st.plugin->PreampParam());
    SetTextColor(hdc, RGB(170, 170, 190));
    TextOutW(hdc, 12, 24, line2, lstrlenW(line2));

    if (!st.plugin->HasPreset()) {
        // --- bannière « aucun preset » (v3.4.1) : le greffon est TRANSPARENT
        // sans preset — état impossible à rater (retour utilisateur réel :
        // « aucune différence de son quand le greffon est activé »).
        const int cx = (kPlotLeft + kPlotRight) / 2;
        const int cy = (kPlotTop + kPlotBottom) / 2;
        if (st.bannerFont != nullptr) {
            SelectObject(hdc, st.bannerFont);
            SetTextAlign(hdc, TA_CENTER);
            SetTextColor(hdc, RGB(255, 159, 64)); // orange franc
            const wchar_t* title = L"Aucun preset chargé — son non corrigé";
            TextOutW(hdc, cx, cy - 34, title, lstrlenW(title));
            SelectObject(hdc, st.font);
            SetTextAlign(hdc, TA_LEFT);
        }
        SetTextAlign(hdc, TA_CENTER);
        SetTextColor(hdc, RGB(200, 200, 215));
        const wchar_t* how1 = L"Cliquez sur « Charger… » ou déposez ici";
        const wchar_t* how2 = L"un preset .json / .ffp exporté de l'application RoomEq";
        TextOutW(hdc, cx, cy - 2, how1, lstrlenW(how1));
        TextOutW(hdc, cx, cy + 18, how2, lstrlenW(how2));
        SetTextAlign(hdc, TA_LEFT);
    } else if (!st.plugin->PresetHasEffect()) {
        // --- bannière « preset plat » (v3.4.2) : un preset est chargé mais
        // toutes ses bandes sont à 0 dB — le son traverse sans correction.
        // Retour utilisateur réel : « le VST affiche 10 bandes mais sur la
        // courbe je ne vois pas les 10 bandes » — les points des bandes
        // tombent sur la ligne 0 dB, l'état doit être explicite.
        const int cx = (kPlotLeft + kPlotRight) / 2;
        const int cy = (kPlotTop + kPlotBottom) / 2;
        if (st.bannerFont != nullptr) {
            SelectObject(hdc, st.bannerFont);
            SetTextAlign(hdc, TA_CENTER);
            SetTextColor(hdc, RGB(255, 159, 64)); // orange franc
            const wchar_t* title = L"Preset plat — le son n'est pas corrigé";
            TextOutW(hdc, cx, cy - 34, title, lstrlenW(title));
            SelectObject(hdc, st.font);
            SetTextAlign(hdc, TA_LEFT);
        }
        SetTextAlign(hdc, TA_CENTER);
        SetTextColor(hdc, RGB(200, 200, 215));
        wchar_t why[128];
        const int nWhy = swprintf(why, 128,
            L"« %s » : %d bandes, toutes à 0 dB (aucun effet)",
            Utf8ToWide(preset.name).c_str(), bands);
        TextOutW(hdc, cx, cy - 2, why, nWhy);
        const wchar_t* how = L"Chargez un preset de correction (bouton « Charger… »)";
        TextOutW(hdc, cx, cy + 18, how, lstrlenW(how));
        SetTextAlign(hdc, TA_LEFT);
    } else {
        SetTextColor(hdc, RGB(120, 120, 140));
        const wchar_t* hint = L"Charger… ou deposer un fichier .json / .ffp";
        TextOutW(hdc, kEditorWidth - 232, kEditorHeight - 17, hint,
            lstrlenW(hint));
    }

    DeleteObject(grid);
    DeleteObject(zero);
}

// ------------------------------------------------------------- fenêtre

LRESULT CALLBACK EditorWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    auto* st = reinterpret_cast<EditorState*>(
        GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    switch (msg) {
    case WM_NCCREATE: {
        const auto* cs = reinterpret_cast<const CREATESTRUCTW*>(lParam);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA,
            reinterpret_cast<LONG_PTR>(cs->lpCreateParams));
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
    case WM_PAINT: {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hwnd, &ps);
        if (st != nullptr)
            DrawEditor(hdc, *st);
        EndPaint(hwnd, &ps);
        return 0;
    }
    case WM_ERASEBKGND:
        return 1; // fond dessiné dans WM_PAINT (pas de scintillement)
    case WM_COMMAND:
        if (LOWORD(wParam) == kButtonId && st != nullptr && st->plugin != nullptr) {
            wchar_t file[MAX_PATH] = L"";
            wchar_t filter[] = L"Presets RoomEq (*.json;*.ffp)\0*.json;*.ffp\0"
                               L"Tous les fichiers (*.*)\0*.*\0";
            OPENFILENAMEW ofn;
            ZeroMemory(&ofn, sizeof(ofn));
            ofn.lStructSize = sizeof(ofn);
            ofn.hwndOwner = hwnd;
            ofn.lpstrFilter = filter;
            ofn.lpstrFile = file;
            ofn.nMaxFile = MAX_PATH;
            ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY;
            if (GetOpenFileNameW(&ofn)) {
                std::string error;
                if (!LoadPresetFromFile(*st, file, error)) {
                    MessageBoxW(hwnd, Utf8ToWide(error).c_str(),
                        L"RoomEq — preset refuse", MB_ICONWARNING | MB_OK);
                }
            }
            return 0;
        }
        return 0;
    case WM_DROPFILES: {
        if (st == nullptr || st->plugin == nullptr)
            return 0;
        const HDROP drop = reinterpret_cast<HDROP>(wParam);
        wchar_t file[MAX_PATH] = L"";
        if (DragQueryFileW(drop, 0, file, MAX_PATH) > 0) {
            std::string error;
            if (!LoadPresetFromFile(*st, file, error)) {
                MessageBoxW(hwnd, Utf8ToWide(error).c_str(),
                    L"RoomEq — preset refuse", MB_ICONWARNING | MB_OK);
            }
        }
        DragFinish(drop);
        return 0;
    }
    case WM_DESTROY:
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
        return 0;
    default:
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}

const wchar_t* EditorClassName()
{
    static bool registered = false;
    static const wchar_t* name = L"RoomEqVstEditor";
    if (!registered) {
        WNDCLASSEXW wc;
        ZeroMemory(&wc, sizeof(wc));
        wc.cbSize = sizeof(wc);
        wc.style = CS_HREDRAW | CS_VREDRAW;
        wc.lpfnWndProc = &EditorWndProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
        wc.hbrBackground = nullptr; // dessiné dans WM_PAINT
        wc.lpszClassName = name;
        registered = RegisterClassExW(&wc) != 0;
    }
    return name;
}

} // namespace

// ------------------------------------------------------------ API éditeur

bool VstEditorOpen(RoomEqVst& plugin, void* systemWindow)
{
    if (plugin.editorState_ != nullptr)
        return true; // déjà ouvert (un éditeur par instance)

    auto* st = new EditorState();
    st->plugin = &plugin;
    st->hwnd = CreateWindowExW(0, EditorClassName(), L"",
        WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS, 0, 0, kEditorWidth,
        kEditorHeight, static_cast<HWND>(systemWindow), nullptr,
        GetModuleHandleW(nullptr), st);
    if (st->hwnd == nullptr) {
        delete st;
        return false;
    }

    st->font = static_cast<HFONT>(GetStockObject(DEFAULT_GUI_FONT));
    st->bannerFont = CreateFontW(20, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
    st->button = CreateWindowExW(0, L"BUTTON", L"Charger…",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON, kPlotLeft,
        kEditorHeight - 30, 92, 24, st->hwnd,
        reinterpret_cast<HMENU>(static_cast<INT_PTR>(kButtonId)),
        GetModuleHandleW(nullptr), nullptr);
    if (st->button != nullptr && st->font != nullptr)
        SendMessageW(st->button, WM_SETFONT, reinterpret_cast<WPARAM>(st->font),
            TRUE);

    DragAcceptFiles(st->hwnd, TRUE);
    plugin.editorState_ = st;
    return true;
}

void VstEditorClose(RoomEqVst& plugin)
{
    auto* st = static_cast<EditorState*>(plugin.editorState_);
    if (st == nullptr)
        return;
    plugin.editorState_ = nullptr;
    if (st->hwnd != nullptr) {
        // WM_DESTROY nettoie le pointeur utilisateur ; DestroyWindow est
        // synchrone — aucun message ne suit son retour.
        DestroyWindow(st->hwnd);
    }
    if (st->bannerFont != nullptr)
        DeleteObject(st->bannerFont); // le stock GUI font, lui, ne se supprime pas
    delete st;
}

void VstEditorIdle(RoomEqVst&)
{
    // rien : ni minuteur ni animation (la courbe est recalculée au tracé)
}

#endif // _WIN32 && !ROOMEQ_VST_NO_EDITOR
