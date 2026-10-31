// RoomEq — modèle de preset du cœur natif (spec I-AUD-07) : miroir de
// RoomEq.Core/Eq/{EqBand,EqPreset}.cs, indépendant de tout format de fichier.
#pragma once

#include "roomeq/dsp/filter_type.h"

#include <string>
#include <vector>

namespace roomeq::preset {

/// Une bande de l'égaliseur paramétrique (les bornes sont appliquées au
/// moment de la construction des filtres, comme dans RoomEq.Core).
struct EqBandDef {
    dsp::FilterType type = dsp::FilterType::Peaking;
    double frequency = 1000.0;
    double gainDb = 0.0;
    double q = 1.1;
    bool enabled = true;
};

/// Un preset complet : nom, préamplification, bandes.
struct PresetDef {
    std::string name = "Sans nom";
    double preampDb = 0.0;
    std::vector<EqBandDef> bands;
};

/// Preset vide transparent (aucune bande, préampli 0 dB).
inline PresetDef MakeIdentityPreset()
{
    PresetDef preset;
    preset.name = "Transparent";
    return preset;
}

} // namespace roomeq::preset
