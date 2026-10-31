// RoomEq — lecture des presets (spec I-AUD-07) : format binaire .ffp Pro-Q 3.
// Inverse exact de RoomEq.Core/Io/FabFilterProQ3.cs : en-tête « FQ3p »,
// version int32 petit-boutiste, nombre de paramètres int32, puis des flotants
// 32 bits LE — 24 bandes de 13 valeurs suivies de 22 paramètres globaux.
#include "roomeq/preset/preset_io.h"

#include <cmath>
#include <cstring>

namespace roomeq::preset {

namespace {

constexpr int kExpectedVersion = 4;
constexpr int kBandCount = 24;
constexpr int kParamsPerBand = 13;
constexpr int kGlobalCount = 22;
constexpr int kExpectedParams = kBandCount * kParamsPerBand + kGlobalCount;

// Encodage du Q (identique à l'écriture) : log10(Q)·0,312098175 + 0,5
constexpr double kQScale = 0.312098175;
constexpr double kOutputGainScale = 36.0;

// Plages acceptées par le greffon (les valeurs sont ramenées aux bornes).
constexpr double kMinFrequencyHz = 10.0;
constexpr double kMaxFrequencyHz = 30000.0;
constexpr double kMinGainDb = -30.0;
constexpr double kMaxGainDb = 30.0;
constexpr double kMinQ = 0.025;
constexpr double kMaxQ = 40.0;

double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

uint32_t ReadU32(const uint8_t* p)
{
    return static_cast<uint32_t>(p[0]) | (static_cast<uint32_t>(p[1]) << 8)
           | (static_cast<uint32_t>(p[2]) << 16) | (static_cast<uint32_t>(p[3]) << 24);
}

int32_t ReadI32(const uint8_t* p) { return static_cast<int32_t>(ReadU32(p)); }

float ReadF32(const uint8_t* p)
{
    const uint32_t bits = ReadU32(p);
    float out;
    static_assert(sizeof(out) == sizeof(bits), "float 32 bits attendu");
    std::memcpy(&out, &bits, sizeof(out));
    return out;
}

/// Code de type Pro-Q 3 → type RoomEq (voir FabFilterProQ3.TypeCode).
dsp::FilterType TypeFromCode(int code)
{
    switch (code) {
    case 1: return dsp::FilterType::LowShelf;
    case 2: return dsp::FilterType::HighPass; // LowCut côté greffon
    case 3: return dsp::FilterType::HighShelf;
    case 4: return dsp::FilterType::LowPass;  // HighCut côté greffon
    case 5: return dsp::FilterType::Notch;
    default: return dsp::FilterType::Peaking; // Bell, BandPass, TiltShelf
    }
}

} // namespace

bool FromFfp(const uint8_t* data, size_t size, PresetDef& out, std::string& error)
{
    out = PresetDef{};

    if (data == nullptr || size < 12) {
        error = "fichier .ffp trop court";
        return false;
    }
    if (!(data[0] == 'F' && data[1] == 'Q' && data[2] == '3' && data[3] == 'p')) {
        error = "magie .ffp attendue (FQ3p)";
        return false;
    }
    const int32_t version = ReadI32(data + 4);
    const int32_t paramCount = ReadI32(data + 8);
    if (version < 1 || version > kExpectedVersion) {
        error = "version de format .ffp non supportée";
        return false;
    }
    if (paramCount != kExpectedParams) {
        error = "nombre de paramètres inattendu (attendu : 334)";
        return false;
    }
    if (size < static_cast<size_t>(12 + paramCount * 4)) {
        error = "fichier .ffp tronqué";
        return false;
    }

    const uint8_t* params = data + 12;
    const auto param = [params](int index) { return ReadF32(params + index * 4); };

    out.name = "Pro-Q 3";
    out.bands.reserve(static_cast<size_t>(kBandCount));

    for (int i = 0; i < kBandCount; ++i) {
        const int base = i * kParamsPerBand;
        const float status = param(base); // 0 = coupée, 1 = active, 2 = désactivée
        if (!(status > 0.5f && status < 1.5f))
            continue; // emplacement inutilisé

        EqBandDef band;
        band.enabled = true;
        band.type = TypeFromCode(static_cast<int>(param(base + 8)));
        band.frequency = Clamp(std::exp2(static_cast<double>(param(base + 2))),
            kMinFrequencyHz, kMaxFrequencyHz);
        band.gainDb = Clamp(static_cast<double>(param(base + 3)), kMinGainDb, kMaxGainDb);
        band.q = Clamp(std::pow(10.0,
                              (static_cast<double>(param(base + 7)) - 0.5) / kQScale),
            kMinQ, kMaxQ);
        out.bands.push_back(band);
    }

    // gain de sortie global (paramètre 312 + 3) → préampli RoomEq
    out.preampDb = Clamp(static_cast<double>(param(kBandCount * kParamsPerBand + 3))
            * kOutputGainScale,
        -kOutputGainScale, kOutputGainScale);

    return true;
}

} // namespace roomeq::preset
