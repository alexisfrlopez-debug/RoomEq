// RoomEq — filtre biquad RBJ en Transposed Direct Form II (spec I-AUD-07).
// Port EXACT de RoomEq.Core/Dsp/BiquadFilter.cs : mêmes formules du RBJ Audio
// EQ Cookbook, mêmes bornes de sécurité, calculs en double précision.
#pragma once

#include "roomeq/dsp/filter_type.h"

#include <algorithm>
#include <cmath>

namespace roomeq::dsp {

/// Coefficients normalisés (a0 = 1).
struct BiquadCoefficients {
    double b0 = 1.0, b1 = 0.0, b2 = 0.0, a1 = 0.0, a2 = 0.0;
};

/// Filtre biquad portant l'état temporel d'UN canal.
class BiquadFilter {
public:
    BiquadFilter() = default;

    BiquadFilter(FilterType type, double fs, double f0, double gainDb, double q)
    {
        Configure(type, fs, f0, gainDb, q);
    }

    /// Remet l'état temporel à zéro (les coefficients sont conservés).
    void Reset() { z1_ = z2_ = 0.0; }

    /// Reconfigure : coefficients RBJ recalculés, état remis à zéro, bornes de
    /// sécurité appliquées — f0 ∈ [1 ; fs·0,499], Q ∈ [0,05 ; 50] (fs doit être
    /// strictement positif ; sinon le filtre reste transparent).
    void Configure(FilterType type, double fs, double f0, double gainDb, double q);

    /// Filtre un échantillon (Transposed Direct Form II).
    double Process(double x)
    {
        const double y = c_.b0 * x + z1_;
        z1_ = c_.b1 * x - c_.a1 * y + z2_;
        z2_ = c_.b2 * x - c_.a2 * y;
        return y;
    }

    /// Module de la réponse fréquentielle |H(e^{jw})| à la fréquence f.
    double MagnitudeAt(double f) const;

    /// Réponse en dB (20·log10|H|) à la fréquence f.
    double ResponseDb(double f) const
    {
        return 20.0 * std::log10(std::max(MagnitudeAt(f), 1e-12));
    }

    /// Taux de la dernière configuration (pour MagnitudeAt).
    double SampleRate() const { return sampleRate_; }

    /// Coefficients courants (tests / diagnostic).
    const BiquadCoefficients& Coefficients() const { return c_; }

private:
    BiquadCoefficients c_;
    double z1_ = 0.0, z2_ = 0.0;
    double sampleRate_ = 48000.0;
};

} // namespace roomeq::dsp
