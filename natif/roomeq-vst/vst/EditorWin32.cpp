// RoomEq VST — éditeur Win32 du greffon (spec I-AUD-08) : fenêtre enfant
// dessinée à la main (aucune ressource, aucune dépendance). v3.5.0 :
//
//   • ÉGALISEUR MODIFIABLE DIRECTEMENT — glisser un point de bande
//     (fréquence + gain), molette sur un point pour le facteur de qualité,
//     double-clic sur la courbe pour ajouter une bande, double-clic sur un
//     point pour l'activer/désactiver, clic droit pour le type/supprimer.
//   • BANQUE CASQUES/ÉCOUTEURS EMBARQUÉE — deux listes déroulantes
//     (marque, modèle) issues de la bibliothèque intégrée de l'application
//     (données AutoEq) : aucune dépendance à l'application installée.
//   • DIAGNOSTIC DU FLUX AUDIO — VU-mètre entrée/sortie (si les barres
//     bougent, l'audio traverse le greffon) et version affichée (pour
//     vérifier que la bonne DLL est installée).
//
// Toutes les entrées sont défensives (échec de création → éditeur absent,
// le greffon reste utilisable via chunks/paramètres) ; le chargement d'un
// preset malformé affiche l'erreur sans jamais planter l'hôte.
#include "RoomEqVst.h"

#if defined(_WIN32) && !defined(ROOMEQ_VST_NO_EDITOR)

#include "editor_geometry.h"
#include "roomeq/headphones/headphone_bank.h"
#include "roomeq/preset/preset_write.h"
#include "roomeq/preset/preset.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

#include <windows.h>
#include <windowsx.h> // GET_X_LPARAM / GET_Y_LPARAM
#include <commdlg.h>
#include <shellapi.h>

namespace {

// ---------------------------------------------------------------- mesurettes
// v3.5.1 : la taille de la fenêtre vient de editor_geometry.h — LA MÊME
// source que le rect déclaré à l'hôte (kEffEditGetRect). Les static_assert
// ci-dessous verrouillent le cadrage : tout contrôle placé hors fenêtre
// casse la compilation au lieu de coupier silencieusement l'interface
// dans le DAW (retour réel v3.5.0 : rect 520×240 vs fenêtre 560×320).
using roomeq_editor::kEditorWidth;
using roomeq_editor::kEditorHeight;

constexpr int kButtonId = 1;        // « Charger… »
constexpr int kBrandComboId = 2;    // marque de casque
constexpr int kModelComboId = 3;    // modèle de casque
constexpr int kApplyId = 4;         // « Appliquer » la correction casque

/// Identifiants du menu contextuel d'une bande.
constexpr int kMenuTypeBase = 100;  // 100+code du type (voir ci-dessous)
constexpr int kMenuToggle = 110;    // activer/désactiver
constexpr int kMenuRemove = 111;    // supprimer

/// Zone de tracé (coordonnées clientes).
constexpr int kPlotLeft = 10, kPlotRight = 550;
constexpr int kPlotTop = 40, kPlotBottom = 232;
/// Plage verticale affichée (dB) — bande de correction typique.
constexpr double kDbTop = 12.0, kDbBottom = -24.0;
/// Rayon de hit-test d'un point de bande (px).
constexpr int kHitRadius = 9;

/// Zone du VU-mètre (coin haut droit) : deux barres IN/OUT.
constexpr int kVuLeft = 442, kVuRight = 548;
constexpr int kVuInY = 8, kVuOutY = 22, kVuHeight = 10;

// --------------------------- cadrage des contrôles (garde-fous v3.5.1)
// Chaque contrôle doit tenir DANS la fenêtre déclarée à l'hôte, sinon le
// DAW le coupe (le rect et la fenêtre partagent editor_geometry.h ; ces
// assertions couvrent le reste de la mise en page).
static_assert(kPlotRight <= kEditorWidth - 10,
    "la zone de tracé déborde à droite");
static_assert(kPlotBottom + 20 <= kEditorHeight - 64,
    "les repères de fréquence empiètent sur la rangée des contrôles");
static_assert(kVuRight <= kEditorWidth - 10,
    "le VU-mètre déborde à droite");
static_assert(kVuOutY + kVuHeight <= kPlotTop - 4,
    "le VU-mètre empiète sur la zone de tracé");
// rangée du bas : bouton Charger… + label + listes casque + Appliquer
static_assert(458 + 92 <= kEditorWidth,
    "le bouton « Appliquer » déborde à droite");
static_assert((kEditorHeight - 42) + 24 <= kEditorHeight,
    "les listes déroulantes débordent en bas");
static_assert((kEditorHeight - 64) + 24 <= kEditorHeight,
    "le bouton « Charger… » déborde en bas");

struct EditorState {
    RoomEqVst* plugin = nullptr;
    HWND hwnd = nullptr;
    HWND button = nullptr;
    HWND brandCombo = nullptr;
    HWND modelCombo = nullptr;
    HWND applyButton = nullptr;
    HFONT font = nullptr;
    HFONT bannerFont = nullptr; // bannière « aucun preset » (gras, orange)

    // édition : bande en cours de glissement / sélectionnée (molette)
    int dragBand = -1;      // index de la bande glissée (−1 aucun)
    int selectedBand = -1;  // bande survolée/sélectionnée (molette Q)

    // VU-mètre : valeurs affichées (décroissance locale côté interface)
    float vuIn = 0.0f;
    float vuOut = 0.0f;
    bool vuDirty = false;
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

/// Inverses (édition) : position écran → fréquence / gain.
double XToFreq(int x)
{
    const double logMin = std::log10(20.0), logMax = std::log10(20000.0);
    const double t = static_cast<double>(x - kPlotLeft)
                     / static_cast<double>(kPlotRight - kPlotLeft);
    return std::pow(10.0, logMin + std::clamp(t, 0.0, 1.0) * (logMax - logMin));
}

double YToDb(int y)
{
    const double t = static_cast<double>(y - kPlotTop)
                     / static_cast<double>(kPlotBottom - kPlotTop);
    return kDbTop - std::clamp(t, 0.0, 1.0) * (kDbTop - kDbBottom);
}

/// Position écran du point d'une bande (gain recalé du préampli affiché).
POINT BandPoint(const roomeq::preset::EqBandDef& band, double preampShift)
{
    POINT p;
    p.x = static_cast<LONG>(FreqToX(band.frequency));
    p.y = static_cast<LONG>(DbToY(band.gainDb + preampShift));
    return p;
}

/// Index de la bande dont le point est le plus proche de (x, y) (−1 si aucun
/// dans le rayon de hit-test). Les bandes désactivées sont éditables aussi.
int HitTestBand(const EditorState& st, int x, int y)
{
    const roomeq::preset::PresetDef preset = st.plugin->CurrentPreset();
    const double preampShift = static_cast<double>(st.plugin->PreampParam())
                               - preset.preampDb;
    int best = -1;
    int bestDist = kHitRadius * kHitRadius;
    for (size_t i = 0; i < preset.bands.size(); ++i) {
        const POINT p = BandPoint(preset.bands[i], preampShift);
        const int dx = p.x - x, dy = p.y - y;
        const int dist = dx * dx + dy * dy;
        if (dist <= bestDist) {
            best = static_cast<int>(i);
            bestDist = dist;
        }
    }
    return best;
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

    // --- points de bandes (v3.4.2) : un disque par bande ACTIVE, un cercle
    // grisé par bande désactivée — glisser pour déplacer, molette pour le Q,
    // double-clic pour activer/désactiver, clic droit pour le menu (v3.5.0)
    for (size_t i = 0; i < preset.bands.size(); ++i) {
        const auto& b = preset.bands[i];
        const POINT p = BandPoint(b, preampShift);
        const bool selected = static_cast<int>(i) == st.selectedBand
            || static_cast<int>(i) == st.dragBand;
        const COLORREF fill = !b.enabled ? RGB(90, 90, 105)
            : selected ? RGB(255, 209, 102)
            : RGB(186, 174, 255);
        const COLORREF edge = !b.enabled ? RGB(120, 120, 135)
            : selected ? RGB(255, 236, 179)
            : RGB(150, 138, 220);
        HPEN dotPen = CreatePen(PS_SOLID, selected ? 2 : 1, edge);
        HBRUSH dotBrush = CreateSolidBrush(fill);
        HPEN oldPen = static_cast<HPEN>(SelectObject(hdc, dotPen));
        HBRUSH oldBrush = static_cast<HBRUSH>(SelectObject(hdc, dotBrush));
        if (b.enabled)
            Ellipse(hdc, p.x - 5, p.y - 5, p.x + 5, p.y + 5);
        else
            Ellipse(hdc, p.x - 4, p.y - 4, p.x + 4, p.y + 4);
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
    TextOutW(hdc, 12, 6, line1, lstrlenW(line1));

    wchar_t line2[128];
    if (st.plugin->PresetHasEffect())
        swprintf(line2, 128,
            L"%d bandes · gain max %.1f dB · preamp %+.1f dB · glisser un point, molette = Q",
            bands, maxGainDb, st.plugin->PreampParam());
    else
        swprintf(line2, 128, L"%d bandes · preamp %+.1f dB",
            bands, st.plugin->PreampParam());
    SetTextColor(hdc, RGB(170, 170, 190));
    TextOutW(hdc, 12, 24, line2, lstrlenW(line2));

    // --- VU-mètre (v3.5.0) : entrée / sortie — si ces barres bougent,
    // l'audio traverse réellement le greffon (diagnostic « aucune
    // différence de son » : VU figé = le greffon ne reçoit pas d'audio).
    const int vuLabelIn = 6, vuLabelOut = 3;
    SetTextColor(hdc, RGB(150, 150, 170));
    TextOutW(hdc, kVuLeft - 38, kVuInY - 2, L"In", vuLabelIn);
    TextOutW(hdc, kVuLeft - 38, kVuOutY - 2, L"Out", vuLabelOut);
    for (int bar = 0; bar < 2; ++bar) {
        const int y = bar == 0 ? kVuInY : kVuOutY;
        RECT frame = { kVuLeft, y, kVuRight, y + kVuHeight };
        FrameRect(hdc, &frame, static_cast<HBRUSH>(GetStockObject(GRAY_BRUSH)));
        const float level = bar == 0 ? st.vuIn : st.vuOut;
        const int w = static_cast<int>((kVuRight - kVuLeft - 2)
            * std::clamp(level, 0.0f, 1.0f));
        if (w > 0) {
            RECT fill = { kVuLeft + 1, y + 1, kVuLeft + 1 + w, y + kVuHeight - 1 };
            HBRUSH brush = CreateSolidBrush(level > 0.99f
                ? RGB(255, 96, 96)
                : level > 0.5f ? RGB(255, 209, 102) : RGB(120, 200, 130));
            FillRect(hdc, &fill, brush);
            DeleteObject(brush);
        }
    }

    // --- version (coin bas-droit) : vérifier la DLL installée en un coup
    // d'œil (retour utilisateur : d'anciennes versions restaient installées).
    SetTextColor(hdc, RGB(110, 110, 130));
    const int vMajor = RoomEqVst::kVersion / 1000;
    const int vMinor = (RoomEqVst::kVersion % 1000) / 100;
    const int vPatch = RoomEqVst::kVersion % 100;
    wchar_t version[24];
    const int nV = swprintf(version, 24, L"v%d.%d.%d", vMajor, vMinor, vPatch);
    TextOutW(hdc, kEditorWidth - 12 - nV * 6, kEditorHeight - 16, version, nV);

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
        const wchar_t* how1 = L"Cliquez sur « Charger… », choisissez un casque ci-dessous";
        const wchar_t* how2 = L"ou double-cliquez sur la courbe pour créer une bande";
        TextOutW(hdc, cx, cy - 2, how1, lstrlenW(how1));
        TextOutW(hdc, cx, cy + 18, how2, lstrlenW(how2));
        SetTextAlign(hdc, TA_LEFT);
    } else if (!st.plugin->PresetHasEffect()) {
        // --- bannière « preset plat » (v3.4.2) : un preset est chargé mais
        // toutes ses bandes sont à 0 dB — le son traverse sans correction.
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
        const wchar_t* how = L"Glissez un point pour corriger, ou chargez un preset";
        TextOutW(hdc, cx, cy + 18, how, lstrlenW(how));
        SetTextAlign(hdc, TA_LEFT);
    }

    DeleteObject(grid);
    DeleteObject(zero);
}

// ------------------------------------------------------- menus de bande

/// Libellé français d'un type de filtre.
const wchar_t* TypeNameLocalized(roomeq::dsp::FilterType type)
{
    using roomeq::dsp::FilterType;
    switch (type) {
    case FilterType::LowShelf: return L"Shelf bas";
    case FilterType::HighShelf: return L"Shelf haut";
    case FilterType::LowPass: return L"Passe-bas";
    case FilterType::HighPass: return L"Passe-haut";
    case FilterType::Notch: return L"Notch (réjection)";
    case FilterType::Peaking: return L"Cloche (peaking)";
    }
    return L"Cloche (peaking)";
}

/// Code de commande du menu ↔ type. Ordre stable : Peaking, LowShelf,
/// HighShelf, LowPass, HighPass, Notch (celui de l'énumération C++).
int TypeToMenuCode(roomeq::dsp::FilterType type)
{
    return static_cast<int>(type);
}

roomeq::dsp::FilterType MenuCodeToType(int code)
{
    switch (code) {
    case 1: return roomeq::dsp::FilterType::LowShelf;
    case 2: return roomeq::dsp::FilterType::HighShelf;
    case 3: return roomeq::dsp::FilterType::LowPass;
    case 4: return roomeq::dsp::FilterType::HighPass;
    case 5: return roomeq::dsp::FilterType::Notch;
    default: return roomeq::dsp::FilterType::Peaking;
    }
}

/// Menu contextuel d'une bande (clic droit) : type, activation, suppression.
void ShowBandMenu(EditorState& st, int bandIndex, int x, int y)
{
    roomeq::preset::EqBandDef band;
    if (!st.plugin->BandAt(static_cast<size_t>(bandIndex), band))
        return;

    HMENU menu = CreatePopupMenu();
    HMENU types = CreatePopupMenu();
    using roomeq::dsp::FilterType;
    const FilterType allTypes[] = { FilterType::Peaking, FilterType::LowShelf,
        FilterType::HighShelf, FilterType::LowPass, FilterType::HighPass,
        FilterType::Notch };
    for (const FilterType t : allTypes) {
        AppendMenuW(types,
            MF_STRING | (t == band.type ? MF_CHECKED : MF_UNCHECKED),
            kMenuTypeBase + TypeToMenuCode(t), TypeNameLocalized(t));
    }
    AppendMenuW(menu, MF_POPUP, reinterpret_cast<UINT_PTR>(types), L"Type");
    AppendMenuW(menu, MF_STRING | (band.enabled ? MF_CHECKED : MF_UNCHECKED),
        kMenuToggle, band.enabled ? L"Active" : L"Désactivée");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, kMenuRemove, L"Supprimer la bande");

    const int choice = TrackPopupMenu(menu,
        TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, x, y, 0, st.hwnd,
        nullptr);
    DestroyMenu(menu);

    const auto index = static_cast<size_t>(bandIndex);
    if (choice >= kMenuTypeBase && choice < kMenuToggle)
        st.plugin->SetBandType(index, MenuCodeToType(choice - kMenuTypeBase));
    else if (choice == kMenuToggle)
        st.plugin->SetBandEnabled(index, !band.enabled);
    else if (choice == kMenuRemove)
        st.plugin->RemoveBand(index);
    else
        return;
    InvalidateRect(st.hwnd, nullptr, TRUE);
}

// ------------------------------------------------------- combos casques

/// Remplit la liste des marques (distinctes, triées) puis les modèles de la
/// première marque. L'item data du combo modèle = index dans la banque.
void FillHeadphoneCombos(EditorState& st)
{
    const auto& bank = roomeq::bank::HeadphoneBank();
    SendMessageW(st.brandCombo, CB_RESETCONTENT, 0, 0);
    SendMessageW(st.modelCombo, CB_RESETCONTENT, 0, 0);

    std::vector<std::string> brands;
    for (const auto& m : bank)
        if (brands.empty() || brands.back() != m.brand)
            brands.push_back(m.brand); // la banque est triée par marque

    for (const auto& b : brands)
        SendMessageW(st.brandCombo, CB_ADDSTRING, 0,
            reinterpret_cast<LPARAM>(Utf8ToWide(b).c_str()));
    SendMessageW(st.brandCombo, CB_SETCURSEL, 0, 0);

    // modèles de la première marque
    const std::string first = brands.empty() ? std::string() : brands[0];
    for (size_t i = 0; i < bank.size(); ++i) {
        if (bank[i].brand == first) {
            const LRESULT pos = SendMessageW(st.modelCombo, CB_ADDSTRING, 0,
                reinterpret_cast<LPARAM>(
                    Utf8ToWide(bank[i].model).c_str()));
            SendMessageW(st.modelCombo, CB_SETITEMDATA, pos,
                static_cast<LPARAM>(i));
        }
    }
    SendMessageW(st.modelCombo, CB_SETCURSEL, 0, 0);
}

/// Marque sélectionnée → recharge la liste des modèles correspondants.
void OnBrandChanged(EditorState& st)
{
    const LRESULT sel = SendMessageW(st.brandCombo, CB_GETCURSEL, 0, 0);
    if (sel == CB_ERR)
        return;
    const int len = static_cast<int>(SendMessageW(st.brandCombo,
        CB_GETLBTEXTLEN, sel, 0));
    if (len <= 0 || len > 256)
        return;
    std::wstring wide(static_cast<size_t>(len) + 1, L'\0');
    SendMessageW(st.brandCombo, CB_GETLBTEXT, sel,
        reinterpret_cast<LPARAM>(wide.data()));
    const std::string brand(wide.begin(), wide.end() - 1); // ASCII attendu

    const auto& bank = roomeq::bank::HeadphoneBank();
    SendMessageW(st.modelCombo, CB_RESETCONTENT, 0, 0);
    for (size_t i = 0; i < bank.size(); ++i) {
        if (bank[i].brand == brand) {
            const LRESULT pos = SendMessageW(st.modelCombo, CB_ADDSTRING, 0,
                reinterpret_cast<LPARAM>(
                    Utf8ToWide(bank[i].model).c_str()));
            SendMessageW(st.modelCombo, CB_SETITEMDATA, pos,
                static_cast<LPARAM>(i));
        }
    }
    SendMessageW(st.modelCombo, CB_SETCURSEL, 0, 0);
}

/// Applique la correction du modèle sélectionné dans les combos.
void OnApplyHeadphone(EditorState& st)
{
    const LRESULT sel = SendMessageW(st.modelCombo, CB_GETCURSEL, 0, 0);
    if (sel == CB_ERR)
        return;
    const LRESULT index = SendMessageW(st.modelCombo, CB_GETITEMDATA, sel, 0);
    if (index == CB_ERR || index < 0
        || static_cast<size_t>(index) >= st.plugin->HeadphoneCount())
        return;
    if (st.plugin->LoadHeadphone(static_cast<size_t>(index)))
        InvalidateRect(st.hwnd, nullptr, TRUE);
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

    // --------------------------------------------- édition à la souris
    case WM_LBUTTONDOWN:
    case WM_LBUTTONDBLCLK: {
        if (st == nullptr || st->plugin == nullptr)
            return 0;
        const int x = GET_X_LPARAM(lParam), y = GET_Y_LPARAM(lParam);
        if (x < kPlotLeft || x > kPlotRight || y < kPlotTop || y > kPlotBottom)
            return 0;
        const int hit = HitTestBand(*st, x, y);
        if (msg == WM_LBUTTONDBLCLK) {
            if (hit >= 0) {
                // double-clic sur un point : activer/désactiver la bande
                roomeq::preset::EqBandDef band;
                if (st->plugin->BandAt(static_cast<size_t>(hit), band))
                    st->plugin->SetBandEnabled(static_cast<size_t>(hit),
                        !band.enabled);
            } else {
                // double-clic sur la courbe : créer une bande ici
                st->plugin->AddBand(XToFreq(x), YToDb(y));
            }
            InvalidateRect(hwnd, nullptr, TRUE);
            return 0;
        }
        if (hit >= 0) {
            st->dragBand = hit;
            st->selectedBand = hit;
            SetCapture(hwnd);
            SetCursor(LoadCursor(nullptr, IDC_SIZEALL));
        }
        return 0;
    }
    case WM_MOUSEMOVE: {
        if (st == nullptr || st->plugin == nullptr)
            return 0;
        const int x = GET_X_LPARAM(lParam), y = GET_Y_LPARAM(lParam);
        if (st->dragBand >= 0) {
            // glissement : fréquence (X) + gain (Y), clamps côté greffon
            const double freq = XToFreq(std::clamp(x, kPlotLeft, kPlotRight));
            const double db = YToDb(std::clamp(y, kPlotTop, kPlotBottom));
            if (st->plugin->EditBand(static_cast<size_t>(st->dragBand),
                    &freq, &db, nullptr) >= 0)
                InvalidateRect(hwnd, nullptr, TRUE);
            return 0;
        }
        // survol : curseur main sur un point de bande
        const int hit = HitTestBand(*st, x, y);
        if (hit != st->selectedBand) {
            st->selectedBand = hit;
            InvalidateRect(hwnd, nullptr, TRUE);
        }
        SetCursor(LoadCursor(nullptr, hit >= 0 ? IDC_HAND : IDC_ARROW));
        return 0;
    }
    case WM_LBUTTONUP:
        if (st != nullptr && st->dragBand >= 0) {
            st->dragBand = -1;
            ReleaseCapture();
            SetCursor(LoadCursor(nullptr, IDC_ARROW));
        }
        return 0;
    case WM_MOUSEWHEEL: {
        // molette sur un point : facteur de qualité (Q ×/÷ pas de 1,12)
        if (st == nullptr || st->plugin == nullptr)
            return 0;
        POINT pt = { GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
        ScreenToClient(hwnd, &pt);
        int hit = HitTestBand(*st, pt.x, pt.y);
        if (hit < 0)
            hit = st->selectedBand; // molette sur la dernière sélection
        if (hit < 0)
            return 0;
        roomeq::preset::EqBandDef band;
        if (!st->plugin->BandAt(static_cast<size_t>(hit), band))
            return 0;
        const int notches = GET_WHEEL_DELTA_WPARAM(wParam) / WHEEL_DELTA;
        const double q = band.q * std::pow(1.12, notches);
        st->selectedBand = hit;
        if (st->plugin->EditBand(static_cast<size_t>(hit), nullptr, nullptr,
                &q) >= 0)
            InvalidateRect(hwnd, nullptr, TRUE);
        return 0;
    }
    case WM_RBUTTONUP: {
        if (st == nullptr || st->plugin == nullptr)
            return 0;
        const int x = GET_X_LPARAM(lParam), y = GET_Y_LPARAM(lParam);
        const int hit = HitTestBand(*st, x, y);
        if (hit >= 0) {
            st->selectedBand = hit;
            ShowBandMenu(*st, hit, x, y);
        }
        return 0;
    }

    // ----------------------------------------------------- commandes
    case WM_COMMAND: {
        if (st == nullptr || st->plugin == nullptr)
            return 0;
        const int id = LOWORD(wParam);
        const int code = HIWORD(wParam);
        if (id == kButtonId && code == BN_CLICKED) {
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
        if (id == kBrandComboId && code == CBN_SELCHANGE) {
            OnBrandChanged(*st);
            return 0;
        }
        if (id == kApplyId && code == BN_CLICKED) {
            OnApplyHeadphone(*st);
            return 0;
        }
        return 0;
    }
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
        wc.style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS; // double-clic éditeur
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
        kEditorHeight - 64, 92, 24, st->hwnd,
        reinterpret_cast<HMENU>(static_cast<INT_PTR>(kButtonId)),
        GetModuleHandleW(nullptr), nullptr);

    // --- rangée casques : libellé + marque + modèle + appliquer (v3.5.0)
    HWND label = CreateWindowExW(0, L"STATIC", L"Casque/écouteurs :",
        WS_CHILD | WS_VISIBLE | SS_SIMPLE, 116, kEditorHeight - 60, 130, 16,
        st->hwnd, nullptr, GetModuleHandleW(nullptr), nullptr);
    st->brandCombo = CreateWindowExW(0, L"COMBOBOX", L"",
        WS_CHILD | WS_VISIBLE | CBS_DROPDOWNLIST | WS_VSCROLL | CBS_SORT,
        116, kEditorHeight - 42, 150, 260, st->hwnd,
        reinterpret_cast<HMENU>(static_cast<INT_PTR>(kBrandComboId)),
        GetModuleHandleW(nullptr), nullptr);
    st->modelCombo = CreateWindowExW(0, L"COMBOBOX", L"",
        WS_CHILD | WS_VISIBLE | CBS_DROPDOWNLIST | WS_VSCROLL,
        272, kEditorHeight - 42, 180, 300, st->hwnd,
        reinterpret_cast<HMENU>(static_cast<INT_PTR>(kModelComboId)),
        GetModuleHandleW(nullptr), nullptr);
    st->applyButton = CreateWindowExW(0, L"BUTTON", L"Appliquer",
        WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON, 458, kEditorHeight - 42, 92, 24,
        st->hwnd, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kApplyId)),
        GetModuleHandleW(nullptr), nullptr);

    for (HWND child : { st->button, st->brandCombo, st->modelCombo,
             st->applyButton, label }) {
        if (child != nullptr && st->font != nullptr)
            SendMessageW(child, WM_SETFONT,
                reinterpret_cast<WPARAM>(st->font), TRUE);
    }
    if (st->brandCombo != nullptr && st->modelCombo != nullptr)
        FillHeadphoneCombos(*st);

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

void VstEditorIdle(RoomEqVst& plugin)
{
    // VU-mètre (v3.5.0) : l'hôte appelle effEditIdle en boucle tant que
    // l'éditeur est ouvert — décroissance locale + rafraîchissement de la
    // seule zone VU (la courbe n'est pas repeinte, pas de scintillement).
    auto* st = static_cast<EditorState*>(plugin.editorState_);
    if (st == nullptr || st->hwnd == nullptr)
        return;
    const float in = plugin.InputLevel();
    const float out = plugin.OutputLevel();
    st->vuIn = std::max(in, st->vuIn * 0.82f);
    st->vuOut = std::max(out, st->vuOut * 0.82f);
    const bool changed = std::fabs(st->vuIn - in) > 0.005f
        || std::fabs(st->vuOut - out) > 0.005f;
    if (changed || st->vuDirty) {
        st->vuDirty = changed;
        RECT vu = { kVuLeft - 40, kVuInY - 3, kVuRight + 2,
            kVuOutY + kVuHeight + 2 };
        InvalidateRect(st->hwnd, &vu, FALSE);
    }
}

#endif // _WIN32 && !ROOMEQ_VST_NO_EDITOR
