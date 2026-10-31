// RoomEq AU — vue Cocoa du greffon (spec I-AUD-07) : chargement d'un preset
// (.json RoomEq ou .ffp Pro-Q 3, bouton ou glisser-déposer), informations du
// preset chargé et courbe de réponse composite. Classe instanciée par l'hôte
// via kAudioUnitProperty_CocoaUI (protocole AUCocoaUIBase). ARC requis.
#import <AppKit/AppKit.h>
#import <AudioUnit/AUCocoaUIView.h>
#import <AudioToolbox/AudioToolbox.h>
#import <CoreFoundation/CoreFoundation.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#import <dispatch/dispatch.h>

#include "RoomEqAu.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <vector>

@class RoomEqAuView;

namespace {

/// Couleurs de l'application RoomEq.
constexpr CGFloat kBgR = 0.071, kBgG = 0.078, kBgB = 0.102;        // #12141A
constexpr CGFloat kCurveR = 1.0, kCurveG = 0.624, kCurveB = 0.110; // #FF9F1C

/// Abcisse log : 20 Hz → 20 kHz sur toute la largeur utile.
CGFloat XForFrequency(CGFloat width, double freq)
{
    constexpr double kLogMin = 1.3010299956639813; // log10(20)
    constexpr double kLogMax = 4.3010299956639814; // log10(20000)
    const double x = (std::log10(freq) - kLogMin) / (kLogMax - kLogMin);
    return static_cast<CGFloat>(12.0 + x * (width - 24.0));
}

} // namespace

/// Courbe de réponse seule (tracée à partir des paires reçues du greffon).
@interface RoomEqCurveView : NSView {
    std::vector<double> _pairs; // fréquence, dB × N
}
- (void)setPairs:(std::vector<double>)pairs;
@end

@implementation RoomEqCurveView

- (BOOL)isFlipped
{
    return YES;
}

- (void)setPairs:(std::vector<double>)pairs
{
    _pairs = std::move(pairs);
    [self setNeedsDisplay:YES];
}

- (void)drawRect:(NSRect)dirtyRect
{
    const NSRect bounds = [self bounds];
    [[NSColor colorWithCalibratedRed:kBgR green:kBgG blue:kBgB alpha:1.0] setFill];
    NSRectFill(bounds);

    const CGFloat height = bounds.size.height;
    const CGFloat width = bounds.size.width;
    const auto yForDb = [&](double db) -> CGFloat {
        const double clamped = db < -24.0 ? -24.0 : (db > 24.0 ? 24.0 : db);
        return static_cast<CGFloat>((24.0 - clamped) / 48.0 * (height - 16.0) + 8.0);
    };

    // grille : ligne 0 dB + décades (100 Hz, 1 kHz, 10 kHz)
    [[NSColor colorWithCalibratedWhite:0.25 alpha:1.0] setStroke];
    NSBezierPath* grid = [NSBezierPath bezierPath];
    [grid setLineWidth:0.5];
    [grid moveToPoint:NSMakePoint(12, yForDb(0))];
    [grid lineToPoint:NSMakePoint(width - 12, yForDb(0))];
    for (double f = 100; f <= 10000; f *= 10) {
        const CGFloat x = XForFrequency(width, f);
        [grid moveToPoint:NSMakePoint(x, 6)];
        [grid lineToPoint:NSMakePoint(x, height - 6)];
    }
    [grid stroke];

    // courbe composite
    if (_pairs.size() >= 4) {
        NSBezierPath* curve = [NSBezierPath bezierPath];
        [curve setLineWidth:1.8];
        [curve moveToPoint:NSMakePoint(XForFrequency(width, _pairs[0]), yForDb(_pairs[1]))];
        const size_t points = _pairs.size() / 2;
        for (size_t i = 1; i < points; ++i)
            [curve lineToPoint:NSMakePoint(XForFrequency(width, _pairs[i * 2]),
                yForDb(_pairs[i * 2 + 1]))];
        [[NSColor colorWithCalibratedRed:kCurveR green:kCurveG blue:kCurveB alpha:1.0]
            setStroke];
        [curve stroke];
    }
}

@end

/// Vue principale du greffon (fabriquée par l'hôte via le protocole
/// AUCocoaUIBase : uiViewForAudioUnit:withSize:).
@interface RoomEqAuView : NSView <AUCocoaUIBase> {
    AudioUnit _au;
    NSTextField* _info;
    RoomEqCurveView* _curve;
}
// protocole AUCocoaUIBase (méthodes d'INSTANCE) : l'hôte instancie cette
// classe par un simple init — l'instance sert de FABRIQUE, et
// uiViewForAudioUnit:withSize: crée la vraie vue configurée
- (unsigned)interfaceVersion;
- (NSView*)uiViewForAudioUnit:(AudioUnit)au withSize:(NSSize)size;
- (void)loadPresetFromURL:(NSURL*)url;
- (void)refreshFromAU;
@end

/// Rappel identique à l'ajout et au retrait (même pointeur de fonction) ;
/// signature conforme à AudioUnitPropertyListenerProc (retour OSStatus).
/// Écoute NOTRE propriété d'état (kRoomEqPropertyStatus) : il n'existe pas
/// de propriété Apple « changement de valeur de paramètre » en AUv2.
static void ParameterChangedCallback(void* inRefCon, AudioUnit /*inUnit*/,
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
    RoomEqAuView* view = [[RoomEqAuView alloc]
        initWithFrame:NSMakeRect(0, 0, size.width > 100 ? size.width : 520,
                                       size.height > 80 ? size.height : 216)];
    [view configureWithAudioUnit:au];
    return view;
}

- (void)configureWithAudioUnit:(AudioUnit)au
{
    _au = au;

    // fond aux couleurs de l'application
    self.wantsLayer = YES;
    self.layer.backgroundColor = [NSColor colorWithCalibratedRed:kBgR green:kBgG
        blue:kBgB alpha:1.0].CGColor;

    // titre
    NSTextField* title = [NSTextField labelWithString:@"RoomEq — Correction"];
    title.font = [NSFont systemFontOfSize:13 weight:NSFontWeightBold];
    title.textColor = [NSColor colorWithCalibratedRed:kCurveR green:kCurveG
        blue:kCurveB alpha:1.0];
    title.frame = NSMakeRect(12, 190, 300, 18);
    [self addSubview:title];

    // informations du preset chargé
    _info = [NSTextField labelWithString:@"Aucun preset chargé (transparent)."];
    _info.font = [NSFont systemFontOfSize:10];
    _info.textColor = [NSColor colorWithCalibratedWhite:0.72 alpha:1.0];
    _info.lineBreakMode = NSLineBreakByTruncatingMiddle;
    _info.frame = NSMakeRect(12, 172, 496, 14);
    [self addSubview:_info];

    // courbe de réponse
    _curve = [[RoomEqCurveView alloc] initWithFrame:NSMakeRect(0, 40, 520, 126)];
    [self addSubview:_curve];

    // bouton de chargement
    NSButton* load = [NSButton buttonWithTitle:@"Charger un preset…"
        target:self action:@selector(openPresetPanel:)];
    load.bezelStyle = NSBezelStyleRounded;
    load.font = [NSFont systemFontOfSize:11];
    load.frame = NSMakeRect(12, 8, 150, 26);
    [self addSubview:load];

    // aide
    NSTextField* hint = [NSTextField labelWithString:
        @"Glissez aussi un .json (RoomEq) ou .ffp (Pro-Q 3) ici — presets de "
        @"l'application : ~/.config/RoomEq/presets"];
    hint.font = [NSFont systemFontOfSize:9];
    hint.textColor = [NSColor colorWithCalibratedWhite:0.55 alpha:1.0];
    hint.frame = NSMakeRect(170, 12, 340, 22);
    [self addSubview:hint];

    // glisser-déposer de fichiers
    [self registerForDraggedTypes:@[ NSPasteboardTypeFileURL ]];

    // suivi des changements d'état du greffon (preset chargé — y compris
    // depuis une autre vue, ou par glisser-déposer)
    AudioUnitAddPropertyListener(_au, kRoomEqPropertyStatus,
        ParameterChangedCallback, (__bridge void*)self);

    [self refreshFromAU];
}

- (void)dealloc
{
    if (_au != nullptr)
        AudioUnitRemovePropertyListenerWithUserData(_au,
            kRoomEqPropertyStatus, ParameterChangedCallback, (__bridge void*)self);
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
    if (data == nil || data.length == 0) {
        _info.stringValue = [NSString stringWithFormat:@"Lecture impossible : %@",
            url.lastPathComponent];
        return;
    }
    CFDataRef blob = (__bridge CFDataRef)data;
    const OSStatus status = AudioUnitSetProperty(_au, kRoomEqPropertyPresetData,
        kAudioUnitScope_Global, 0, &blob, sizeof(blob));
    if (status != noErr) {
        _info.stringValue = [NSString stringWithFormat:@"Preset refusé (%d) : %@",
            static_cast<int>(status), url.lastPathComponent];
    }
    [self refreshFromAU];
}

// ------------------------------------------------------------ affichage

- (void)refreshFromAU
{
    CFDictionaryRef status = nullptr;
    UInt32 size = sizeof(status);
    if (AudioUnitGetProperty(_au, kRoomEqPropertyStatus, kAudioUnitScope_Global, 0,
            &status, &size) == noErr
        && status != nullptr) {
        const CFStringRef name = static_cast<CFStringRef>(
            CFDictionaryGetValue(status, CFSTR("name")));
        const CFNumberRef bands = static_cast<CFNumberRef>(
            CFDictionaryGetValue(status, CFSTR("bands")));
        const CFNumberRef preamp = static_cast<CFNumberRef>(
            CFDictionaryGetValue(status, CFSTR("preampDb")));
        int bandCount = 0;
        double preampDb = 0;
        if (bands != nullptr)
            CFNumberGetValue(bands, kCFNumberIntType, &bandCount);
        if (preamp != nullptr)
            CFNumberGetValue(preamp, kCFNumberDoubleType, &preampDb);
        NSString* nameStr = name != nullptr ? (__bridge NSString*)name : @"?";
        _info.stringValue = [NSString
            stringWithFormat:@"%@ — %d bande(s) active(s) — préampli %+.1f dB",
            nameStr, bandCount, preampDb];
        CFRelease(status);
    }

    // courbe de réponse composite
    CFDataRef curveData = nullptr;
    size = sizeof(curveData);
    if (AudioUnitGetProperty(_au, kRoomEqPropertyCurve, kAudioUnitScope_Global, 0,
            &curveData, &size) == noErr
        && curveData != nullptr) {
        const size_t count = static_cast<size_t>(CFDataGetLength(curveData)) / sizeof(double);
        std::vector<double> pairs(count);
        if (count > 0)
            std::memcpy(pairs.data(), CFDataGetBytePtr(curveData), count * sizeof(double));
        [_curve setPairs:std::move(pairs)];
        CFRelease(curveData);
    }
}

// ------------------------------------------------------------ glisser-déposer

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
