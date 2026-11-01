// RoomEq VST — banque de corrections casques/écouteurs embarquée (v3.5.0).
// Données : headphone_bank.h (généré depuis la bibliothèque intégrée de
// l'application, données AutoEq — MIT). Le greffon n'exige PAS que
// l'application soit installée : la banque vit dans la DLL.
#pragma once

#include "roomeq/dsp/filter_type.h"
#include "roomeq/preset/preset.h"

#include <string>
#include <vector>

namespace roomeq_vst {

/// Un filtre d'une correction de casque (types produits par AutoEq : peaking,
/// low-shelf, high-shelf — les mêmes que la bibliothèque C#).
struct HeadphoneFilterDef {
    roomeq::dsp::FilterType type = roomeq::dsp::FilterType::Peaking;
    double frequency = 1000.0;
    double gainDb = 0.0;
    double q = 1.0;
};

/// Un modèle de la banque embarquée.
struct HeadphoneModelDef {
    std::string id;
    std::string brand;
    std::string model;
    std::string source;
    int typeCode = 0; // 0 over-ear, 1 in-ear, 2 earbud, 3 gaming
    double preampDb = 0.0;
    std::vector<HeadphoneFilterDef> filters;
};

/// Toute la banque, décodée (une fois — appel depuis le thread interface).
/// Retourne un vecteur vide si le blob embarqué est inattendu (ne plante
/// jamais : l'appelant affiche simplement une banque vide).
const std::vector<HeadphoneModelDef>& HeadphoneBank();

/// Libellé français du type (« Casque circum-aural », « Écouteurs intra »…).
const char* HeadphoneTypeLabel(int typeCode);

/// Convertit un modèle en preset d'égaliseur — miroir EXACT de
/// HeadphonePresetLibrary.ToEqPreset (clamps identiques : préampli
/// [-15, 0], fréquence [20, 20000], gain [-24, 24], Q [0.1, 20]).
roomeq::preset::PresetDef HeadphonePreset(const HeadphoneModelDef& model);

} // namespace roomeq_vst
