// RoomEq — ABI VST 2.4 minimale, autodéclarée (spec I-AUD-08 / décision D21).
//
// Le SDK VST 2 n'est plus distribué ni concédé par Steinberg : ce fichier ne
// copie AUCUN en-tête Steinberg. Il déclare uniquement ce que le greffon
// utilise, avec des commentaires et une organisation propres. Chaque
// constante a été vérifiée valeur à valeur par double recoupement : SDK 2.4
// authentique (lecture de contrôle uniquement) + en-tête publique vestige
// (LMMS/falkTX) — la structure AEffect et la numérotation des opcodes sont
// des faits d'interface publics, stables depuis 1999.
//
// Portée volontairement MINIMALE : pas de MIDI, pas de synchronisation
// temporelle, pas d'offline, pas d'arrangement de haut-parleurs. Un opcode
// non déclaré ci-dessous n'est pas géré par le greffon (réponse 0).
#pragma once

#include <cstdint>

namespace vst {

// ------------------------------------------------------------------- types

/// Entier de la taille d'un pointeur — le type pivot de l'ABI (32 bits sur
/// les hôtes 32 bits, 64 sur les 64 ; les pointeurs de fonctions aussi).
typedef intptr_t IntPtr;

struct AEffect;

/// Rappel hôte → greffon : le hôte fournit ce callback à VSTPluginMain.
typedef IntPtr (*HostCallback)(AEffect* effect, int32_t opcode, int32_t index,
    IntPtr value, void* ptr, float opt);

typedef IntPtr (*DispatcherProc)(AEffect* effect, int32_t opcode, int32_t index,
    IntPtr value, void* ptr, float opt);
typedef void (*ProcessProc)(AEffect* effect, float** inputs, float** outputs,
    int32_t sampleFrames);
typedef void (*ProcessDoubleProc)(AEffect* effect, double** inputs,
    double** outputs, int32_t sampleFrames);
typedef void (*SetParameterProc)(AEffect* effect, int32_t index, float value);
typedef float (*GetParameterProc)(AEffect* effect, int32_t index);

/// La structure d'effet VST 1.0/2.x — ordre de champs EXACT, jamais changé
/// par compatibilité ascendante. 96 octets en 32 bits.
struct AEffect {
    int32_t magic;              // kEffectMagic 'VstP'
    DispatcherProc dispatcher;  // hôte → greffon
    ProcessProc process;        // mode accumulation (déprécié 2.4)
    SetParameterProc setParameter;
    GetParameterProc getParameter;
    int32_t numPrograms;
    int32_t numParams;
    int32_t numInputs;
    int32_t numOutputs;
    int32_t flags;              // EffectFlags
    IntPtr resvd1;              // réservé hôte (doit rester 0)
    IntPtr resvd2;              // réservé hôte (doit rester 0)
    int32_t initialDelay;       // latence en échantillons (0 pour nous)
    int32_t uniqueID;           // identifiant 4 octets, stable entre versions
    int32_t version;            // ex. 3400 pour 3.4.0
    ProcessProc processReplacing;
    ProcessDoubleProc processDoubleReplacing; // VST 2.4 (non utilisé ici)
    char future[56];            // réservé (zéro)
};

/// Rectangle de l'éditeur (coordonnées locales, entiers 16 bits).
struct ERect {
    int16_t top, left, bottom, right;
};

// ------------------------------------------------------------- constantes

/// Constante quatre caractères (boutisme indépendant : plus grand octet en
/// tête, comme l'exige l'ABI).
constexpr int32_t CConst(char a, char b, char c, char d)
{
    return (static_cast<int32_t>(a) << 24) | (static_cast<int32_t>(b) << 16)
         | (static_cast<int32_t>(c) << 8) | static_cast<int32_t>(d);
}

constexpr int32_t kEffectMagic = CConst('V', 's', 't', 'P'); // 0x56737450
constexpr int32_t kVstVersion = 2400;   // version du protocole annoncée

// Tailles maximales des chaînes (octets, terminateur compris).
constexpr int kMaxProgNameLen = 24;     // effGetProgramName / SetProgramName
constexpr int kMaxParamStrLen = 8;      // effGetParamName / Label / Display
constexpr int kMaxEffectNameLen = 32;   // effGetEffectName
constexpr int kMaxVendorStrLen = 64;    // effGetVendorString
constexpr int kMaxProductStrLen = 64;   // effGetProductString

// Catégories de greffon (effGetPlugCategory).
constexpr int32_t kPlugCategoryEffect = 1; // « Simple Effect »

// ------------------------------------------------------------ flags effet

constexpr int32_t kEffFlagsHasEditor = 1;          // éditeur intégré
constexpr int32_t kEffFlagsCanReplacing = 1 << 4;  // processReplacing
constexpr int32_t kEffFlagsProgramChunks = 1 << 5; // état par blob (chunk)
constexpr int32_t kEffFlagsNoSoundInStop = 1 << 9; // (non posé ici)

// -------------------------------------------------- opcodes greffon (hôte
// appelle AEffect::dispatcher) — numérotation du SDK 2.4, les entrées
// dépréciées occupent toujours leur rang (p. ex. effGetVu = 9).

enum EffectOpcodes {
    kEffOpen = 0,
    kEffClose = 1,
    kEffSetProgram = 2,             // [value] numéro de programme
    kEffGetProgram = 3,             // [retour] numéro courant
    kEffSetProgramName = 4,         // [ptr] nouveau nom
    kEffGetProgramName = 5,         // [ptr] tampon 24 octets
    kEffGetParamLabel = 6,          // [ptr] tampon 8 octets (unité)
    kEffGetParamDisplay = 7,        // [ptr] tampon 8 octets (valeur)
    kEffGetParamName = 8,           // [ptr] tampon 8 octets (nom)
    kEffSetSampleRate = 10,         // [opt] taux en Hz
    kEffSetBlockSize = 11,          // [value] bloc max en échantillons
    kEffMainsChanged = 12,          // [value] 1 = resume, 0 = suspend
    kEffEditGetRect = 13,           // [ptr] ERect**
    kEffEditOpen = 14,              // [ptr] fenêtre système (HWND)
    kEffEditClose = 15,
    kEffEditIdle = 19,
    kEffGetChunk = 23,              // [ptr] void** ; [index] 0 bank, 1 prog
    kEffSetChunk = 24,              // [ptr] données ; [value] taille ; [index]
    kEffGetPlugCategory = 35,       // [retour] catégorie
    kEffSetBypass = 44,             // [value] 1 = bypass
    kEffGetEffectName = 45,         // [ptr] tampon 32 octets
    kEffGetVendorString = 47,       // [ptr] tampon 64 octets
    kEffGetProductString = 48,      // [ptr] tampon 64 octets
    kEffGetVendorVersion = 49,      // [retour] version
    kEffCanDo = 51,                 // [ptr] chaîne ; 1 oui, -1 non, 0 ?
    kEffGetTailSize = 52,           // [retour] queue en échantillons
    kEffGetVstVersion = 58,         // [retour] 2400
    kEffStartProcess = 71,
    kEffStopProcess = 72
};

// ------------------------------------------------- opcodes hôte (le greffon
// appelle le HostCallback fourni) — seuls ceux utilisés sont déclarés.

enum HostOpcodes {
    kAudioMasterAutomate = 0,       // [index] paramètre ; [opt] valeur
    kAudioMasterVersion = 1,        // [retour] version protocole hôte
    kAudioMasterIdle = 3,           // traitement des événements interface
    kAudioMasterUpdateDisplay = 42  // rafraîchir l'affichage (changement preset)
};

} // namespace vst
