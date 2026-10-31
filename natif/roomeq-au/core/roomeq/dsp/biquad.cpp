// RoomEq — filtre biquad RBJ (spec I-AUD-07) : implémentation, port exact de
// RoomEq.Core/Dsp/BiquadFilter.cs.
#include "roomeq/dsp/biquad.h"

#include <algorithm>

namespace {
constexpr double kTwoPi = 6.283185307179586476925286766559;
}

namespace roomeq::dsp {

void BiquadFilter::Configure(
    FilterType type, double fs, double f0, double gainDb, double q)
{
    if (!(fs > 0.0)) {
        // taux invalide : filtre transparent (robustesse greffon ; le C# lève,
        // ici l'appelant garantit un taux réel de flux audio)
        c_ = BiquadCoefficients{};
        z1_ = z2_ = 0.0;
        sampleRate_ = 48000.0;
        return;
    }

    f0 = std::clamp(f0, 1.0, fs * 0.499);
    q = std::clamp(q, 0.05, 50.0);
    sampleRate_ = fs;

    const double A = std::pow(10.0, gainDb / 40.0);
    const double w0 = kTwoPi * f0 / fs;
    const double cw = std::cos(w0), sw = std::sin(w0);
    const double alpha = sw / (2.0 * q);
    const double twoSqrtAalpha = 2.0 * std::sqrt(A) * alpha;

    double b0, b1, b2, a0, a1, a2;
    switch (type) {
    case FilterType::Peaking:
        b0 = 1.0 + alpha * A;
        b1 = -2.0 * cw;
        b2 = 1.0 - alpha * A;
        a0 = 1.0 + alpha / A;
        a1 = -2.0 * cw;
        a2 = 1.0 - alpha / A;
        break;

    case FilterType::LowShelf:
        b0 = A * ((A + 1.0) - (A - 1.0) * cw + twoSqrtAalpha);
        b1 = 2.0 * A * ((A - 1.0) - (A + 1.0) * cw);
        b2 = A * ((A + 1.0) - (A - 1.0) * cw - twoSqrtAalpha);
        a0 = (A + 1.0) + (A - 1.0) * cw + twoSqrtAalpha;
        a1 = -2.0 * ((A - 1.0) + (A + 1.0) * cw);
        a2 = (A + 1.0) + (A - 1.0) * cw - twoSqrtAalpha;
        break;

    case FilterType::HighShelf:
        b0 = A * ((A + 1.0) + (A - 1.0) * cw + twoSqrtAalpha);
        b1 = -2.0 * A * ((A - 1.0) + (A + 1.0) * cw);
        b2 = A * ((A + 1.0) + (A - 1.0) * cw - twoSqrtAalpha);
        a0 = (A + 1.0) - (A - 1.0) * cw + twoSqrtAalpha;
        a1 = 2.0 * ((A - 1.0) - (A + 1.0) * cw);
        a2 = (A + 1.0) - (A - 1.0) * cw - twoSqrtAalpha;
        break;

    case FilterType::LowPass:
        b0 = (1.0 - cw) / 2.0;
        b1 = 1.0 - cw;
        b2 = (1.0 - cw) / 2.0;
        a0 = 1.0 + alpha;
        a1 = -2.0 * cw;
        a2 = 1.0 - alpha;
        break;

    case FilterType::HighPass:
        b0 = (1.0 + cw) / 2.0;
        b1 = -(1.0 + cw);
        b2 = (1.0 + cw) / 2.0;
        a0 = 1.0 + alpha;
        a1 = -2.0 * cw;
        a2 = 1.0 - alpha;
        break;

    case FilterType::Notch:
        b0 = 1.0;
        b1 = -2.0 * cw;
        b2 = 1.0;
        a0 = 1.0 + alpha;
        a1 = -2.0 * cw;
        a2 = 1.0 - alpha;
        break;
    }

    c_.b0 = b0 / a0;
    c_.b1 = b1 / a0;
    c_.b2 = b2 / a0;
    c_.a1 = a1 / a0;
    c_.a2 = a2 / a0;
    z1_ = z2_ = 0.0;
}

double BiquadFilter::MagnitudeAt(double f) const
{
    const double w = kTwoPi * f / sampleRate_;
    const double cw = std::cos(w), sw = std::sin(w);
    // H(e^{jw}) = (b0 + b1·z⁻¹ + b2·z⁻²) / (1 + a1·z⁻¹ + a2·z⁻²)
    const double nr = c_.b0 + c_.b1 * cw + c_.b2 * std::cos(2.0 * w);
    const double ni = -(c_.b1 * sw + c_.b2 * std::sin(2.0 * w));
    const double dr = 1.0 + c_.a1 * cw + c_.a2 * std::cos(2.0 * w);
    const double di = -(c_.a1 * sw + c_.a2 * std::sin(2.0 * w));
    const double num2 = nr * nr + ni * ni;
    const double den2 = dr * dr + di * di;
    return std::sqrt(num2 / std::max(den2, 1e-30));
}

} // namespace roomeq::dsp
