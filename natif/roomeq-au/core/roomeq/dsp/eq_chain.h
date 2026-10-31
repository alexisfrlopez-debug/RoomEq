// RoomEq — chaîne d'égalisation temps réel (spec I-AUD-07). Port EXACT de
// RoomEq.Core/Eq/EqChain.cs : N canaux entrelacés traversent chacun la cascade
// des bandes actives ; la préamplification est appliquée en tête (gain
// linéaire, seuil |dB| > 1e-9) ; bypass = signal inchangé.
#pragma once

#include "roomeq/dsp/biquad.h"
#include "roomeq/preset/preset.h"

#include <vector>

namespace roomeq::dsp {

class EqChain {
public:
    /// Définit le format de travail et reconstruit les filtres si nécessaire.
    void Configure(double sampleRate, int channels);

    /// Remplace le preset actif (reconstruction complète des filtres).
    void SetPreset(const preset::PresetDef& preset);

    /// Met à jour la préamplification sans reconstruire (temps réel sûr).
    void SetPreampDb(double preampDb);

    bool Bypass() const { return bypass_; }
    void SetBypass(bool bypass) { bypass_ = bypass; }

    /// Remet à zéro l'état temporel de tous les filtres.
    void Reset();

    /// Traite un buffer entrelacé en place — aucun lock, aucune allocation.
    void Process(float* buffer, size_t sampleCount);

    /// Réponse composite de l'égaliseur en dB à la fréquence donnée
    /// (préampli inclus), pour l'affichage de la courbe.
    double ResponseDb(double freq) const;

    double SampleRate() const { return sampleRate_; }
    int Channels() const { return channels_; }
    double PreampDb() const { return preampDb_; }
    /// Nombre de bandes actives du preset courant.
    size_t ActiveBandCount() const { return bands_.size(); }

private:
    struct BandSpec {
        FilterType type;
        double frequency, gainDb, q;
    };

    void Rebuild();

    double sampleRate_ = 48000.0;
    int channels_ = 2;
    bool bypass_ = false;
    double preampDb_ = 0.0;
    double preampGain_ = 1.0;
    std::vector<BandSpec> bands_;                     // bandes actives uniquement
    std::vector<std::vector<BiquadFilter>> filters_;  // [canal][bande]
};

} // namespace roomeq::dsp
