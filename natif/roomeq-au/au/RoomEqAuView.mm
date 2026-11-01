// RoomEq AU — vue Cocoa du greffon (spec I-AUD-07 v2) : ÉDITITEUR COMPLET,
// miroir de l'éditeur Win32 du greffon VST (EditorWin32.cpp, v3.5.x) :
//
//   • ÉGALISEUR MODIFIABLE DIRECTEMENT — glisser un point de bande
//     (fréquence + gain), molette sur un point pour le facteur de qualité,
//     double-clic sur la courbe pour ajouter une bande, double-clic sur un
//     point pour l'activer/désactiver, clic droit pour le type/supprimer.
//   • BANQUE CASQUES/ÉCOUTEURS EMBARQUÉE — deux listes déroulantes
//     (marque, modèle) issues de la bibliothèque intégrée de l'application
//     (données AutoEq) : aucune dépendance à l'application installée.
//   • DIAGNOSTIC DU FLUX AUDIO — VU-mètre entrée/sortie (si les barres
//     bougent, l'audio traverse le greffon) et version affichée.
//
// Toutes les entrées sont défensives (échec de propriété → étiquette
// d'erreur, jamais de plantage de l'hôte) ; le chargement d'un preset
// malformé affiche l'erreur. Classe instanciée par l'hôte via
// kAudioUnitProperty_CocoaUI (protocole AUCocoaUIBase). ARC requis.
#import <AppKit/AppKit.h>
#import <AudioUnit/AUCocoaUIView.h>
#import <AudioToolbox/AudioToolbox.h>
#import <CoreFoundation/CoreFoundation.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#import <dispatch/dispatch.h>

#include "RoomEqAu.h"
#include "roomeq/headphones/headphone_bank.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <string>
#include <vector>

@class RoomEqAuView;

namespace {

// --------------------------------------------- géométrie (miroir EditorWin32)
// Les constantes reproduisent EXACTEMENT la mise en page de l'éditeur VST
// (EditorWin32.cpp / editor_geometry.h) : parité visuelle entre greffons.
constexpr int kEditorWidth = 560;
constexpr int kEditorHeight = 320;

constexpr int kPlotLeft = 10, kPlotRight = 550;
constexpr int kPlotTop = 40, kPlotBottom = 232;
constexpr double kDbTop = 12.0, kDbBottom = -24.0;
constexpr int kHitRadius = 9;

constexpr int kVuLeft = 442, kVuRight = 548;
constexpr int kVuInY = 8, kVuOutY = 22, kVuHeight = 10;

/// Seuil d'inaudibilité (dB) — jumeau du cœur (preset_editor.cpp).
constexpr double kInaudibleDb = 0.05;

double FreqToX(double freq)
{
    const double logMin = std::log10(20.0), logMax = std::log10(20000.0);
    const double t = (std::log10(std::max(freq, 20.0)) - logMin) / (logMax - logMin);
    return kPlotLeft + t * (kPlotRight - kPlotLeft);
}

double DbToY(double db)
{
    const double clamped = db < kDbBottom ? kDbBottom : (db > kDbTop ? kDbTop : db);
    const double t = (kDbTop - clamped) / (kDbTop - kDbBottom);
    return kPlotTop + t * (kPlotBottom - kPlotTop);
}

/// Inverses (édition) : position vue → fréquence / gain.
double XToFreq(double x)
{
    const double logMin = std::log10(20.0), logMax = std::log10(20000.0);
    const double t = (x - kPlotLeft) / static_cast<double>(kPlotRight - kPlotLeft);
    return std::pow(10.0, logMin + std::clamp(t, 0.0, 1.0) * (logMax - logMin));
}

double YToDb(double y)
{
    const double t = (y - kPlotTop) / static_cast<double>(kPlotBottom - kPlotTop);
    return kDbTop - std::clamp(t, 0.0, 1.0) * (kDbTop - kDbBottom);
}

NSColor* RoomEqColor(int r, int g, int b)
{
    return [NSColor colorWithCalibratedRed:r / 255.0 green:g / 255.0
        blue:b / 255.0 alpha:1.0];
}

void DrawString(NSString* text, NSPoint at, NSColor* color, NSFont* font)
{
    NSDictionary* attrs = @{ NSFontAttributeName: font,
        NSForegroundColorAttributeName: color };
    [text drawAtPoint:at withAttributes:attrs];
}

void DrawStringCentered(NSString* text, double cx, double y, NSColor* color,
    NSFont* font)
{
    NSDictionary* attrs = @{ NSFontAttributeName: font,
        NSForegroundColorAttributeName: color };
    const NSSize size = [text sizeWithAttributes:attrs];
    [text drawAtPoint:NSMakePoint(cx - size.width * 0.5, y) withAttributes:attrs];
}

/// Libellé français d'un type de filtre (miroir EditorWin32).
NSString* TypeNameLocalized(uint32_t type)
{
    switch (type) {
    case 1: return @"Shelf bas";
    case 2: return @"Shelf haut";
    case 3: return @"Passe-bas";
    case 4: return @"Passe-haut";
    case 5: return @"Notch (réjection)";
    default: return @"Cloche (peaking)";
    }
}

/// Une bande décodée de l'instantané « RBP1 » (kRoomEqPropertyBands).
struct BandInfo {
    uint32_t type = 0; // roomeq::dsp::FilterType (Peaking=0 … Notch=5)
    double frequency = 1000.0;
    double gainDb = 0.0;
    double q = 1.0;
    bool enabled = true;
};

/// Une bande ACTIVE est-elle transparente ? (jumeau du cœur — les types
/// filtrants coupent le signal même à 0 dB de gain).
bool BandIsTransparentUI(const BandInfo& band)
{
    switch (band.type) {
    case 3: // LowPass
    case 4: // HighPass
    case 5: // Notch
        return false;
    default:
        return std::fabs(band.gainDb) <= kInaudibleDb;
    }
}

} // namespace

/// Déclarations internes (extension de classe) : sans elles, le compilateur
/// supposerait un retour `id` pour les méthodes non déclarées — les
/// signatures typées (double, BOOL, C++) exigent une déclaration explicite.
@interface RoomEqAuView ()
- (void)fillHeadphonePopups;
- (void)refillModelPopup;
- (double)preampParameter;
- (void)pullStatus;
- (void)pullBands;
- (void)pullCurve;
- (BOOL)sendProperty:(AudioUnitPropertyID)prop payload:(const void*)bytes
    length:(size_t)length;
- (void)sendEditBand:(uint32_t)index mask:(uint8_t)mask freq:(double)freq
    gain:(double)gain q:(double)q;
- (void)sendAddBandAt:(NSPoint)point;
- (void)sendBandOp:(uint32_t)index op:(uint32_t)op type:(uint32_t)type;
- (int)hitTestBandAt:(NSPoint)point;
@end

/// Vue principale du greffon (fabriquée par l'hôte via le protocole
/// AUCocoaUIBase : uiViewForAudioUnit:withSize:).
@interface RoomEqAuView : NSView <AUCocoaUIBase> {
    AudioUnit _au;
    NSButton* _loadButton;
    NSPopUpButton* _brandPopup;
    NSPopUpButton* _modelPopup;
    NSButton* _applyButton;
    NSTrackingArea* _tracking;
    NSTimer* _vuTimer;

    // état affiché (rafraîchi depuis le greffon)
    std::vector<BandInfo> _bands;   // bandes du preset (dessin + édition)
    std::vector<double> _curve;     // courbe : paires (Hz, dB)
    double _presetPreampDb;         // préampli du PRESET (décalage des points)
    bool _hasPreset;                // un preset est chargé (sinon transparent)
    NSString* _presetName;

    // édition : bande glissée / sélectionnée (molette), accumulateur molette
    int _dragBand;
    int _selectedBand;
    double _wheelAccum;

    // VU-mètre : valeurs affichées (décroissance locale côté interface)
    float _vuIn;
    float _vuOut;
}
// protocole AUCocoaUIBase (méthodes d'INSTANCE) : l'hôte instancie cette
// classe par un simple init — l'instance sert de FABRIQUE, et
// uiViewForAudioUnit:withSize: crée la vraie vue configurée
- (unsigned)interfaceVersion;
- (NSView*)uiViewForAudioUnit:(AudioUnit)au withSize:(NSSize)size;
- (void)configureWithAudioUnit:(AudioUnit)au;
- (void)refreshFromAU;
- (void)pollLevels:(NSTimer*)timer;
- (void)loadPresetFromURL:(NSURL*)url;
- (void)bandMenuAction:(NSMenuItem*)sender;
- (void)openPresetPanel:(NSButton*)sender;
- (void)brandChanged:(NSPopUpButton*)sender;
- (void)applyHeadphone:(NSButton*)sender;
@end

/// Rappel identique à l'ajout et au retrait (même pointeur de fonction) ;
/// signature conforme à AudioUnitPropertyListenerProc (retour OSStatus).
/// Écoute NOTRE propriété d'état (kRoomEqPropertyStatus) : il n'existe pas
/// de propriété Apple « changement de valeur de paramètre » en AUv2.
static void PropertyChangedCallback(void* inRefCon, AudioUnit /*inUnit*/,
    AudioUnitPropertyID /*inID*/, AudioUnitScope /*inScope*/,
    AudioUnitElement /*inElement*/)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        RoomEqAuView* view = (__bridge RoomEqAuView*)inRefCon;
        [view refreshFromAU];
    });
}

@implementation RoomEqAuView

- (unsigned)interfaceVersion
{
    return 0;
}

- (NSView*)uiViewForAudioUnit:(AudioUnit)au withSize:(NSSize)size
{
    // taille FIXE (miroir de l'éditeur VST — l'hôte dimensionne sa fenêtre
    // sur la vue retournée ; la mise en page est absolue, 560×320)
    (void)size;
    RoomEqAuView* view = [[RoomEqAuView alloc]
        initWithFrame:NSMakeRect(0, 0, kEditorWidth, kEditorHeight)];
    [view configureWithAudioUnit:au];
    return view;
}

- (void)configureWithAudioUnit:(AudioUnit)au
{
    _au = au;
    _dragBand = -1;
    _selectedBand = -1;
    _presetName = @"Transparent";

    // fond aux couleurs de l'application
    self.wantsLayer = YES;
    self.layer.backgroundColor = RoomEqColor(20, 20, 30).CGColor;

    // ---- rangée du bas : chargement + banque casques + appliquer
    _loadButton = [NSButton buttonWithTitle:@"Charger…"
        target:self action:@selector(openPresetPanel:)];
    _loadButton.bezelStyle = NSBezelStyleRounded;
    _loadButton.font = [NSFont systemFontOfSize:11];
    _loadButton.frame = NSMakeRect(10, kEditorHeight - 64, 92, 24);
    [self addSubview:_loadButton];

    NSTextField* label = [NSTextField labelWithString:@"Casque/écouteurs :"];
    label.font = [NSFont systemFontOfSize:10];
    label.textColor = RoomEqColor(170, 170, 190);
    label.frame = NSMakeRect(116, kEditorHeight - 58, 150, 16);
    [self addSubview:label];

    _brandPopup = [[NSPopUpButton alloc]
        initWithFrame:NSMakeRect(116, kEditorHeight - 42, 150, 26) pullsDown:NO];
    _brandPopup.font = [NSFont systemFontOfSize:11];
    _brandPopup.target = self;
    _brandPopup.action = @selector(brandChanged:);
    [self addSubview:_brandPopup];

    _modelPopup = [[NSPopUpButton alloc]
        initWithFrame:NSMakeRect(272, kEditorHeight - 42, 180, 26) pullsDown:NO];
    _modelPopup.font = [NSFont systemFontOfSize:11];
    [self addSubview:_modelPopup];

    _applyButton = [NSButton buttonWithTitle:@"Appliquer"
        target:self action:@selector(applyHeadphone:)];
    _applyButton.bezelStyle = NSBezelStyleRounded;
    _applyButton.font = [NSFont systemFontOfSize:11];
    _applyButton.frame = NSMakeRect(458, kEditorHeight - 42, 92, 24);
    [self addSubview:_applyButton];

    [self fillHeadphonePopups];

    // glisser-déposer de fichiers presets
    [self registerForDraggedTypes:@[ NSPasteboardTypeFileURL ]];

    // suivi des changements d'état du greffon (chargement, édition — y
    // compris depuis une autre vue ou par glisser-déposer)
    AudioUnitAddPropertyListener(_au, kRoomEqPropertyStatus,
        PropertyChangedCallback, (__bridge void*)self);

    // VU-mètres : sondage des niveaux (30 Hz, decay local — l'audio écrit,
    // l'interface lit). Bloc faible : pas de cycle de rétention.
    __weak RoomEqAuView* weakSelf = self;
    _vuTimer = [NSTimer scheduledTimerWithTimeInterval:0.03 repeats:YES
        block:^(NSTimer* /*timer*/) {
            [weakSelf pollLevels:nil];
        }];

    [self refreshFromAU];
}

- (void)dealloc
{
    if (_vuTimer != nil)
        [_vuTimer invalidate];
    if (_au != nullptr)
        AudioUnitRemovePropertyListenerWithUserData(_au,
            kRoomEqPropertyStatus, PropertyChangedCallback, (__bridge void*)self);
}

// ------------------------------------------------------------ géométrie

- (BOOL)isFlipped
{
    return YES; // y vers le bas — mise en page identique à l'éditeur Win32
}

- (BOOL)acceptsFirstResponder
{
    return YES;
}

- (void)updateTrackingAreas
{
    [super updateTrackingAreas];
    if (_tracking != nil)
        [self removeTrackingArea:_tracking];
    _tracking = [[NSTrackingArea alloc] initWithRect:[self bounds]
        options:NSTrackingMouseEnteredAndExited | NSTrackingMouseMoved
            | NSTrackingActiveInActiveApp
        owner:self userInfo:nil];
    [self addTrackingArea:_tracking];
}

// ------------------------------------------------------------ données

/// Préampli du PARAMÈTRE (source de vérité du gain, automatisable).
- (double)preampParameter
{
    AudioUnitParameterValue value = 0.0f;
    if (AudioUnitGetParameter(_au, RoomEqAu::kParamPreampDb,
            kAudioUnitScope_Global, 0, &value) != noErr)
        return 0.0;
    return value;
}

- (void)pullStatus
{
    CFDictionaryRef status = nullptr;
    UInt32 size = sizeof(status);
    if (AudioUnitGetProperty(_au, kRoomEqPropertyStatus, kAudioUnitScope_Global, 0,
            &status, &size) == noErr
        && status != nullptr) {
        const CFBooleanRef has = static_cast<CFBooleanRef>(
            CFDictionaryGetValue(status, CFSTR("hasPreset")));
        if (has != nullptr)
            _hasPreset = CFBooleanGetValue(has);
        CFRelease(status);
    }
}

- (void)pullBands
{
    CFDataRef data = nullptr;
    UInt32 size = sizeof(data);
    if (AudioUnitGetProperty(_au, kRoomEqPropertyBands, kAudioUnitScope_Global, 0,
            &data, &size) != noErr
        || data == nullptr)
        return;

    const uint8_t* p = CFDataGetBytePtr(data);
    const size_t n = static_cast<size_t>(CFDataGetLength(data));
    size_t pos = 0;
    const auto read = [&](void* out, size_t count) -> bool {
        if (count > n - pos)
            return false;
        std::memcpy(out, p + pos, count);
        pos += count;
        return true;
    };

    uint8_t magic[4] = { 0, 0, 0, 0 };
    uint32_t version = 0, count = 0;
    uint8_t nameLen = 0;
    std::vector<BandInfo> bands;
    double preamp = 0.0;
    std::string name;
    bool ok = read(magic, 4) && magic[0] == 'R' && magic[1] == 'B'
        && magic[2] == 'P' && magic[3] == '1'
        && read(&version, 4) && read(&preamp, 8) && read(&nameLen, 1);
    if (ok) {
        (void)version; // décodée pour la forme, non affichée
        name.resize(nameLen);
        ok = read(name.data(), nameLen) && read(&count, 4);
    }
    if (ok && count <= 64) { // garde-fou d'affichage
        bands.reserve(count);
        for (uint32_t i = 0; i < count && ok; ++i) {
            BandInfo band;
            uint8_t enabled = 0;
            ok = read(&band.type, 4) && read(&band.frequency, 8)
                && read(&band.gainDb, 8) && read(&band.q, 8)
                && read(&enabled, 1);
            band.enabled = enabled != 0;
            bands.push_back(band);
        }
    }
    if (ok) {
        _bands = std::move(bands);
        _presetPreampDb = preamp;
        _presetName = @(name.c_str());
        if (_selectedBand >= static_cast<int>(_bands.size()))
            _selectedBand = -1;
        if (_dragBand >= static_cast<int>(_bands.size()))
            _dragBand = -1;
    }
    CFRelease(data);
}

- (void)pullCurve
{
    CFDataRef data = nullptr;
    UInt32 size = sizeof(data);
    if (AudioUnitGetProperty(_au, kRoomEqPropertyCurve, kAudioUnitScope_Global, 0,
            &data, &size) == noErr
        && data != nullptr) {
        const size_t count = static_cast<size_t>(CFDataGetLength(data)) / sizeof(double);
        std::vector<double> pairs(count);
        if (count > 0)
            std::memcpy(pairs.data(), CFDataGetBytePtr(data), count * sizeof(double));
        _curve = std::move(pairs);
        CFRelease(data);
    }
}

- (void)refreshFromAU
{
    [self pullStatus];
    [self pullBands];
    [self pullCurve];
    [self setNeedsDisplay:YES];
}

// ------------------------------------------------- envoi des commandes

- (BOOL)sendProperty:(AudioUnitPropertyID)prop payload:(const void*)bytes
    length:(size_t)length
{
    if (bytes == nullptr || length == 0)
        return NO;
    NSData* data = [NSData dataWithBytes:bytes length:length];
    if (data == nil)
        return NO;
    CFDataRef blob = (__bridge CFDataRef)data;
    return AudioUnitSetProperty(_au, prop, kAudioUnitScope_Global, 0, &blob,
               sizeof(blob)) == noErr;
}

- (void)sendEditBand:(uint32_t)index mask:(uint8_t)mask freq:(double)freq
    gain:(double)gain q:(double)q
{
    uint8_t payload[32];
    std::memcpy(payload, &index, 4);
    payload[4] = mask;
    std::memcpy(payload + 5, &freq, 8);
    std::memcpy(payload + 13, &gain, 8);
    std::memcpy(payload + 21, &q, 8);
    [self sendProperty:kRoomEqPropertyEditBand payload:payload length:29];
}

- (void)sendAddBandAt:(NSPoint)point
{
    uint8_t payload[16];
    const double freq = XToFreq(std::clamp(static_cast<double>(point.x),
        static_cast<double>(kPlotLeft), static_cast<double>(kPlotRight)));
    const double gain = YToDb(std::clamp(static_cast<double>(point.y),
        static_cast<double>(kPlotTop), static_cast<double>(kPlotBottom)));
    std::memcpy(payload, &freq, 8);
    std::memcpy(payload + 8, &gain, 8);
    [self sendProperty:kRoomEqPropertyAddBand payload:payload length:16];
}

- (void)sendBandOp:(uint32_t)index op:(uint32_t)op type:(uint32_t)type
{
    uint8_t payload[12];
    std::memcpy(payload, &index, 4);
    std::memcpy(payload + 4, &op, 4);
    std::memcpy(payload + 8, &type, 4);
    [self sendProperty:kRoomEqPropertyBandOp payload:payload length:12];
}

// ------------------------------------------------------------- édition

- (int)hitTestBandAt:(NSPoint)point
{
    const double preampShift = [self preampParameter] - _presetPreampDb;
    int best = -1;
    double bestDist = kHitRadius * kHitRadius;
    for (size_t i = 0; i < _bands.size(); ++i) {
        const double x = FreqToX(_bands[i].frequency);
        const double y = DbToY(_bands[i].gainDb + preampShift);
        const double dx = x - point.x, dy = y - point.y;
        const double dist = dx * dx + dy * dy;
        if (dist <= bestDist) {
            best = static_cast<int>(i);
            bestDist = dist;
        }
    }
    return best;
}

- (void)mouseDown:(NSEvent*)event
{
    const NSPoint p = [self convertPoint:event.locationInWindow fromView:nil];
    if (p.x < kPlotLeft || p.x > kPlotRight || p.y < kPlotTop || p.y > kPlotBottom)
        return;
    const int hit = [self hitTestBandAt:p];
    if (event.clickCount >= 2) {
        if (hit >= 0) {
            // double-clic sur un point : activer/désactiver la bande
            [self sendBandOp:static_cast<uint32_t>(hit) op:1 type:0];
        } else {
            // double-clic sur la courbe : créer une bande ici
            [self sendAddBandAt:p];
        }
        [self refreshFromAU];
        return;
    }
    if (hit >= 0) {
        _dragBand = hit;
        _selectedBand = hit;
        [[NSCursor closedHandCursor] set];
    }
}

- (void)mouseDragged:(NSEvent*)event
{
    if (_dragBand < 0)
        return;
    const NSPoint p = [self convertPoint:event.locationInWindow fromView:nil];
    // glissement : fréquence (X) + gain (Y), clamps côté greffon
    const double freq = XToFreq(std::clamp(static_cast<double>(p.x),
        static_cast<double>(kPlotLeft), static_cast<double>(kPlotRight)));
    const double gain = YToDb(std::clamp(static_cast<double>(p.y),
        static_cast<double>(kPlotTop), static_cast<double>(kPlotBottom)));
    [self sendEditBand:static_cast<uint32_t>(_dragBand) mask:0x3
        freq:freq gain:gain q:0.0];
    [self refreshFromAU];
}

- (void)mouseUp:(NSEvent*)event
{
    if (_dragBand >= 0) {
        _dragBand = -1;
        [[NSCursor arrowCursor] set];
    }
}

- (void)mouseMoved:(NSEvent*)event
{
    const NSPoint p = [self convertPoint:event.locationInWindow fromView:nil];
    const int hit = [self hitTestBandAt:p];
    if (hit != _selectedBand) {
        _selectedBand = hit;
        [self setNeedsDisplay:YES];
    }
    [[NSCursor pointingHandCursor] set];
}

- (void)mouseExited:(NSEvent*)event
{
    if (_selectedBand != -1) {
        _selectedBand = -1;
        [self setNeedsDisplay:YES];
    }
    [[NSCursor arrowCursor] set];
}

- (void)scrollWheel:(NSEvent*)event
{
    // molette sur un point : facteur de qualité (Q ×/÷ pas de 1,12). Les
    // trackpads émettent de petits deltas : accumulation par cran entier.
    _wheelAccum += event.deltaY;
    int notches = 0;
    while (_wheelAccum >= 1.0) {
        ++notches;
        _wheelAccum -= 1.0;
    }
    while (_wheelAccum <= -1.0) {
        --notches;
        _wheelAccum += 1.0;
    }
    if (notches == 0)
        return;

    const NSPoint p = [self convertPoint:event.locationInWindow fromView:nil];
    int hit = [self hitTestBandAt:p];
    if (hit < 0)
        hit = _selectedBand; // molette sur la dernière sélection
    if (hit < 0 || static_cast<size_t>(hit) >= _bands.size())
        return;
    const double q = _bands[static_cast<size_t>(hit)].q * std::pow(1.12, notches);
    _selectedBand = hit;
    [self sendEditBand:static_cast<uint32_t>(hit) mask:0x4
        freq:0.0 gain:0.0 q:q];
    [self refreshFromAU];
}

- (void)rightMouseDown:(NSEvent*)event
{
    const NSPoint p = [self convertPoint:event.locationInWindow fromView:nil];
    const int hit = [self hitTestBandAt:p];
    if (hit < 0 || static_cast<size_t>(hit) >= _bands.size())
        return;
    _selectedBand = hit;
    const BandInfo& band = _bands[static_cast<size_t>(hit)];

    // menu contextuel : type (sous-menu), activation, suppression — les
    // étiquettes de commande codent bande et opération (tag = bande<<8|code)
    NSMenu* menu = [[NSMenu alloc] init];
    NSMenuItem* typeItem = [menu addItemWithTitle:@"Type" action:NULL keyEquivalent:@""];
    NSMenu* types = [[NSMenu alloc] init];
    for (uint32_t t = 0; t <= 5; ++t) {
        NSMenuItem* item = [types addItemWithTitle:TypeNameLocalized(t)
            action:@selector(bandMenuAction:) keyEquivalent:@""];
        item.target = self;
        item.tag = (hit << 8) | (10 + static_cast<int>(t));
        item.state = (t == band.type) ? NSControlStateValueOn : NSControlStateValueOff;
    }
    [menu setSubmenu:types forItem:typeItem];

    NSMenuItem* toggle = [menu addItemWithTitle:(band.enabled ? @"Active" : @"Désactivée")
        action:@selector(bandMenuAction:) keyEquivalent:@""];
    toggle.target = self;
    toggle.tag = (hit << 8) | 100;
    toggle.state = band.enabled ? NSControlStateValueOn : NSControlStateValueOff;

    [menu addItem:[NSMenuItem separatorItem]];
    NSMenuItem* remove = [menu addItemWithTitle:@"Supprimer la bande"
        action:@selector(bandMenuAction:) keyEquivalent:@""];
    remove.target = self;
    remove.tag = (hit << 8) | 101;

    [menu popUpMenuPositioningItem:nil atLocation:p inView:self];
    [self setNeedsDisplay:YES];
}

- (void)bandMenuAction:(NSMenuItem*)sender
{
    const int band = sender.tag >> 8;
    const int code = sender.tag & 0xff;
    if (band < 0)
        return;
    const auto index = static_cast<uint32_t>(band);
    if (code >= 10 && code <= 15) {
        [self sendBandOp:index op:2 type:static_cast<uint32_t>(code - 10)];
    } else if (code == 100) {
        [self sendBandOp:index op:1 type:0];
    } else if (code == 101) {
        [self sendBandOp:index op:0 type:0];
    } else {
        return;
    }
    [self refreshFromAU];
}

// ------------------------------------------------------- combos casques

- (void)fillHeadphonePopups
{
    // la banque vit dans le même bundle que la vue (statique du composant) :
    // lecture directe, aucune propriété nécessaire pour des données figées.
    const auto& bank = roomeq::bank::HeadphoneBank();

    [_brandPopup removeAllItems];
    NSMutableArray<NSString*>* brands = [NSMutableArray array];
    for (const auto& m : bank) {
        NSString* brand = @(m.brand.c_str());
        if (brands.count == 0 || ![brands.lastObject isEqualToString:brand])
            [brands addObject:brand]; // la banque est triée par marque
    }
    [_brandPopup addItemsWithTitles:brands];
    [self refillModelPopup];
}

- (void)refillModelPopup
{
    NSString* brand = _brandPopup.titleOfSelectedItem;
    if (brand == nil)
        return;
    const auto& bank = roomeq::bank::HeadphoneBank();
    [_modelPopup removeAllItems];
    for (size_t i = 0; i < bank.size(); ++i) {
        if ([brand isEqualToString:@(bank[i].brand.c_str())]) {
            NSMenuItem* item = [_modelPopup
                addItemWithTitle:@(bank[i].model.c_str())
                action:NULL keyEquivalent:@""];
            item.representedObject = [NSNumber numberWithUnsignedLong:i];
        }
    }
}

- (void)brandChanged:(NSPopUpButton*)sender
{
    [self refillModelPopup];
}

- (void)applyHeadphone:(NSButton*)sender
{
    NSMenuItem* item = _modelPopup.selectedItem;
    if (item == nil || item.representedObject == nil)
        return;
    const uint32_t index = static_cast<uint32_t>(
        [(NSNumber*)item.representedObject unsignedLongValue]);
    uint8_t payload[4];
    std::memcpy(payload, &index, 4);
    [self sendProperty:kRoomEqPropertyLoadHeadphone payload:payload length:4];
    [self refreshFromAU];
}

// ------------------------------------------------------------ chargement

- (void)openPresetPanel:(NSButton*)sender
{
    NSOpenPanel* panel = [NSOpenPanel openPanel];
    panel.canChooseDirectories = NO;
    panel.canChooseFiles = YES;
    panel.allowsMultipleSelection = NO;
    panel.title = @"Charger un preset RoomEq";
    if (@available(macOS 11.0, *)) {
        UTType* ffp = [UTType typeWithFilenameExtension:@"ffp"];
        NSMutableArray<UTType*>* types = [NSMutableArray arrayWithObject:UTTypeJSON];
        if (ffp != nil)
            [types addObject:ffp];
        panel.allowedContentTypes = types;
    } else {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
        // cible de déploiement 11.0 : branche jamais prise, conservée pour la
        // compilation avec un SDK plus ancien
        panel.allowedFileTypes = @[ @"json", @"ffp" ];
#pragma clang diagnostic pop
    }

    // répertoire par défaut : presets de l'application RoomEq (.NET)
    NSString* presets = [NSHomeDirectory()
        stringByAppendingPathComponent:@".config/RoomEq/presets"];
    if ([[NSFileManager defaultManager] fileExistsAtPath:presets])
        panel.directoryURL = [NSURL fileURLWithPath:presets];

    if ([panel runModal] == NSModalResponseOK && panel.URL != nil)
        [self loadPresetFromURL:panel.URL];
}

- (void)loadPresetFromURL:(NSURL*)url
{
    NSData* data = [NSData dataWithContentsOfURL:url];
    if (data == nil || data.length == 0)
        return;
    CFDataRef blob = (__bridge CFDataRef)data;
    const OSStatus status = AudioUnitSetProperty(_au, kRoomEqPropertyPresetData,
        kAudioUnitScope_Global, 0, &blob, sizeof(blob));
    (void)status; // l'état retourné par refreshFromAU reflète le résultat
    [self refreshFromAU];
}

// ------------------------------------------------------------ VU-mètres

- (void)pollLevels:(NSTimer*)timer
{
    if (_au == nullptr)
        return;
    CFDataRef data = nullptr;
    UInt32 size = sizeof(data);
    if (AudioUnitGetProperty(_au, kRoomEqPropertyLevels, kAudioUnitScope_Global, 0,
            &data, &size) != noErr
        || data == nullptr)
        return;
    if (CFDataGetLength(data) >= static_cast<CFIndex>(2 * sizeof(float))) {
        float levels[2] = { 0.0f, 0.0f };
        std::memcpy(levels, CFDataGetBytePtr(data), sizeof(levels));
        _vuIn = std::max(levels[0], _vuIn * 0.82f);
        _vuOut = std::max(levels[1], _vuOut * 0.82f);
        [self setNeedsDisplayInRect:NSMakeRect(kVuLeft - 40, 4,
            (kVuRight - kVuLeft) + 44, kVuOutY + kVuHeight + 2)];
    }
    CFRelease(data);
}

// ------------------------------------------------------------- dessin

- (void)drawRect:(NSRect)dirtyRect
{
    const NSRect bounds = [self bounds];
    [RoomEqColor(20, 20, 30) setFill];
    NSRectFill(bounds);

    // --- grille : verticale 100 Hz / 1 kHz / 10 kHz, horizontale par 12 dB
    NSBezierPath* grid = [NSBezierPath bezierPath];
    [grid setLineWidth:1.0];
    for (double f : { 100.0, 1000.0, 10000.0 }) {
        const double x = FreqToX(f);
        [grid moveToPoint:NSMakePoint(x, kPlotTop)];
        [grid lineToPoint:NSMakePoint(x, kPlotBottom)];
    }
    for (double db = kDbTop; db >= kDbBottom - 0.5; db -= 12.0) {
        const double y = DbToY(db);
        [grid moveToPoint:NSMakePoint(kPlotLeft, y)];
        [grid lineToPoint:NSMakePoint(kPlotRight, y)];
    }
    [RoomEqColor(58, 58, 84) setStroke];
    [grid stroke];

    // ligne 0 dB en avant-plan
    NSBezierPath* zero = [NSBezierPath bezierPath];
    [zero setLineWidth:1.0];
    [zero moveToPoint:NSMakePoint(kPlotLeft, DbToY(0.0))];
    [zero lineToPoint:NSMakePoint(kPlotRight, DbToY(0.0))];
    [RoomEqColor(96, 96, 128) setStroke];
    [zero stroke];

    // repères de fréquence + « 0 dB »
    NSFont* small = [NSFont systemFontOfSize:10];
    NSColor* labelColor = RoomEqColor(150, 150, 170);
    DrawString(@"20 Hz", NSMakePoint(kPlotLeft + 2, kPlotBottom + 3), labelColor, small);
    DrawString(@"100", NSMakePoint(FreqToX(100.0) - 10, kPlotBottom + 3), labelColor, small);
    DrawString(@"1k", NSMakePoint(FreqToX(1000.0) - 5, kPlotBottom + 3), labelColor, small);
    DrawString(@"10k", NSMakePoint(FreqToX(10000.0) - 10, kPlotBottom + 3), labelColor, small);
    DrawString(@"0 dB", NSMakePoint(kPlotRight - 46, DbToY(0.0) - 16), labelColor, small);

    // --- courbe composite (97 points, préampli du paramètre inclus)
    if (_curve.size() >= 4) {
        NSBezierPath* curve = [NSBezierPath bezierPath];
        [curve setLineWidth:2.0];
        [curve moveToPoint:NSMakePoint(FreqToX(_curve[0]), DbToY(_curve[1]))];
        const size_t points = _curve.size() / 2;
        for (size_t i = 1; i < points; ++i)
            [curve lineToPoint:NSMakePoint(FreqToX(_curve[i * 2]),
                DbToY(_curve[i * 2 + 1]))];
        [RoomEqColor(140, 122, 246) setStroke];
        [curve stroke];
    }

    // --- points de bandes : un disque par bande ACTIVE, un cercle grisé par
    // bande désactivée — glisser pour déplacer, molette pour le Q, double-clic
    // pour activer/désactiver, clic droit pour le menu
    const double preampShift = [self preampParameter] - _presetPreampDb;
    for (size_t i = 0; i < _bands.size(); ++i) {
        const auto& b = _bands[i];
        const double x = FreqToX(b.frequency);
        const double y = DbToY(b.gainDb + preampShift);
        const bool selected = static_cast<int>(i) == _selectedBand
            || static_cast<int>(i) == _dragBand;
        NSColor* fill = !b.enabled ? RoomEqColor(90, 90, 105)
            : selected ? RoomEqColor(255, 209, 102)
                       : RoomEqColor(186, 174, 255);
        NSColor* edge = !b.enabled ? RoomEqColor(120, 120, 135)
            : selected ? RoomEqColor(255, 236, 179)
                       : RoomEqColor(150, 138, 220);
        const double r = b.enabled ? 5.0 : 4.0;
        NSBezierPath* dot = [NSBezierPath
            bezierPathWithOvalInRect:NSMakeRect(x - r, y - r, r * 2, r * 2)];
        [fill setFill];
        [dot fill];
        [edge setStroke];
        [dot setLineWidth:1.0];
        [dot stroke];
    }

    // --- bandeau d'information (miroir EditorWin32)
    int bands = 0;
    double maxGainDb = 0.0;
    bool hasEffect = std::fabs(_presetPreampDb) > kInaudibleDb;
    for (const auto& b : _bands) {
        if (!b.enabled)
            continue;
        ++bands;
        maxGainDb = std::max(maxGainDb, std::fabs(b.gainDb));
        if (!BandIsTransparentUI(b))
            hasEffect = true;
    }
    const double preamp = [self preampParameter];
    NSFont* info = [NSFont systemFontOfSize:11];
    DrawString([NSString stringWithFormat:@"Preset : %@", _presetName],
        NSMakePoint(12, 6), RoomEqColor(235, 235, 245), info);
    NSString* line2 = hasEffect
        ? [NSString stringWithFormat:
              @"%d bandes · gain max %.1f dB · preamp %+.1f dB · glisser un point, molette = Q",
              bands, maxGainDb, preamp]
        : [NSString stringWithFormat:@"%d bandes · preamp %+.1f dB", bands, preamp];
    DrawString(line2, NSMakePoint(12, 24), RoomEqColor(170, 170, 190), info);

    // --- VU-mètre (coin haut droit) : entrée / sortie — si ces barres
    // bougent, l'audio traverse réellement le greffon (diagnostic « aucune
    // différence de son » : VU figé = le greffon ne reçoit pas d'audio).
    DrawString(@"In", NSMakePoint(kVuLeft - 38, kVuInY - 2), labelColor, small);
    DrawString(@"Out", NSMakePoint(kVuLeft - 38, kVuOutY - 2), labelColor, small);
    for (int bar = 0; bar < 2; ++bar) {
        const double y = bar == 0 ? kVuInY : kVuOutY;
        const NSRect frame = NSMakeRect(kVuLeft, y, kVuRight - kVuLeft, kVuHeight);
        [RoomEqColor(90, 90, 105) setStroke];
        NSBezierPath* box = [NSBezierPath bezierPathWithRect:frame];
        [box setLineWidth:1.0];
        [box stroke];
        const float level = bar == 0 ? _vuIn : _vuOut;
        const double w = (kVuRight - kVuLeft - 2)
            * std::clamp(static_cast<double>(level), 0.0, 1.0);
        if (w > 0) {
            NSColor* color = level > 0.99f ? RoomEqColor(255, 96, 96)
                : level > 0.5f ? RoomEqColor(255, 209, 102)
                               : RoomEqColor(120, 200, 130);
            [color setFill];
            NSRectFill(NSMakeRect(kVuLeft + 1, y + 1, w, kVuHeight - 2));
        }
    }

    // --- version (coin bas-droit) : vérifier le composant installé d'un
    // coup d'œil (retour utilisateur VST : d'anciennes versions restaient
    // installées).
    NSString* version = @("v" ROOMEQ_AU_VERSION);
    NSDictionary* vAttrs = @{ NSFontAttributeName: small,
        NSForegroundColorAttributeName: RoomEqColor(110, 110, 130) };
    const NSSize vSize = [version sizeWithAttributes:vAttrs];
    [version drawAtPoint:NSMakePoint(kEditorWidth - 12 - vSize.width,
                               kEditorHeight - 16) withAttributes:vAttrs];

    // --- bannières d'état (impossibles à rater — retours utilisateurs VST
    // v3.4.1/v3.4.2 transposés)
    const double cx = (kPlotLeft + kPlotRight) / 2.0;
    const double cy = (kPlotTop + kPlotBottom) / 2.0;
    NSFont* bannerFont = [NSFont boldSystemFontOfSize:16];
    if (!_hasPreset) {
        // aucun preset : le greffon est TRANSPARENT — son non corrigé
        DrawStringCentered(@"Aucun preset chargé — son non corrigé", cx, cy - 34,
            RoomEqColor(255, 159, 64), bannerFont);
        DrawStringCentered(@"Cliquez sur « Charger… », choisissez un casque ci-dessous",
            cx, cy - 2, RoomEqColor(200, 200, 215), info);
        DrawStringCentered(@"ou double-cliquez sur la courbe pour créer une bande",
            cx, cy + 18, RoomEqColor(200, 200, 215), info);
    } else if (!hasEffect) {
        // preset chargé mais toutes ses bandes à 0 dB — non corrigé
        DrawStringCentered(@"Preset plat — le son n'est pas corrigé", cx, cy - 34,
            RoomEqColor(255, 159, 64), bannerFont);
        DrawStringCentered([NSString
            stringWithFormat:@"« %@ » : %d bandes, toutes à 0 dB (aucun effet)",
            _presetName, bands], cx, cy - 2, RoomEqColor(200, 200, 215), info);
        DrawStringCentered(@"Glissez un point pour corriger, ou chargez un preset",
            cx, cy + 18, RoomEqColor(200, 200, 215), info);
    }
}

// ------------------------------------------------------- glisser-déposer

- (NSDragOperation)draggingEntered:(id<NSDraggingInfo>)sender
{
    return NSDragOperationCopy;
}

- (BOOL)performDragOperation:(id<NSDraggingInfo>)sender
{
    NSArray<NSURL*>* urls = [[sender draggingPasteboard]
        readObjectsForClasses:@[ [NSURL class] ] options:nil];
    if (urls.count == 0)
        return NO;
    [self loadPresetFromURL:urls.firstObject];
    return YES;
}

@end
