// RoomEq — décodage de la banque casques embarquée (v3.5.0 VST, partagée
// par les greffons AU et VST depuis la v3.6.0 — décision D23).
// Le format binaire « RHP1 » est produit par scripts/gen_headphone_bank.py ;
// ce décodeur est volontairement défensif (bornes vérifiées à chaque pas :
// un blob corrompu donne une banque tronquée, jamais un accès hors zone).
#include "roomeq/headphones/headphone_bank.h"
#include "roomeq/headphones/headphone_bank_data.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>

namespace roomeq::bank {
namespace {

/// Curseur de lecture borné sur le blob embarqué.
struct Reader {
    const unsigned char* data;
    size_t size;
    size_t pos = 0;

    bool Bytes(size_t count, const unsigned char*& out)
    {
        if (count > size - pos)
            return false;
        out = data + pos;
        pos += count;
        return true;
    }

    bool U8(unsigned char& out)
    {
        const unsigned char* p;
        if (!Bytes(1, p))
            return false;
        out = *p;
        return true;
    }

    bool U16(unsigned& out)
    {
        const unsigned char* p;
        if (!Bytes(2, p))
            return false;
        out = static_cast<unsigned>(p[0]) | (static_cast<unsigned>(p[1]) << 8);
        return true;
    }

    bool F32(double& out)
    {
        const unsigned char* p;
        if (!Bytes(4, p))
            return false;
        // little-endien indépendant de l'hôte
        uint32_t bits = static_cast<uint32_t>(p[0])
            | (static_cast<uint32_t>(p[1]) << 8)
            | (static_cast<uint32_t>(p[2]) << 16)
            | (static_cast<uint32_t>(p[3]) << 24);
        float value;
        std::memcpy(&value, &bits, sizeof(value));
        out = static_cast<double>(value);
        return true;
    }

    bool Str(std::string& out)
    {
        unsigned char len;
        if (!U8(len))
            return false;
        const unsigned char* p;
        if (!Bytes(len, p))
            return false;
        out.assign(reinterpret_cast<const char*>(p), len);
        return true;
    }
};

roomeq::dsp::FilterType FilterTypeFromCode(unsigned char code)
{
    switch (code) {
    case 1: return roomeq::dsp::FilterType::LowShelf;
    case 2: return roomeq::dsp::FilterType::HighShelf;
    default: return roomeq::dsp::FilterType::Peaking;
    }
}

std::vector<HeadphoneModelDef> DecodeBank()
{
    std::vector<HeadphoneModelDef> models;
    Reader r{ kHeadphoneBankData, kHeadphoneBankSize };
    const unsigned char* magic;
    if (!r.Bytes(4, magic) || std::memcmp(magic, "RHP1", 4) != 0)
        return models;
    unsigned count;
    if (!r.U16(count))
        return models;
    models.reserve(count);
    for (unsigned i = 0; i < count; ++i) {
        HeadphoneModelDef m;
        unsigned char typeCode = 0, filterCount = 0;
        if (!r.Str(m.id) || !r.Str(m.brand) || !r.Str(m.model)
            || !r.U8(typeCode) || !r.Str(m.source) || !r.F32(m.preampDb)
            || !r.U8(filterCount))
            break;
        m.typeCode = typeCode;
        m.filters.reserve(filterCount);
        for (unsigned char f = 0; f < filterCount; ++f) {
            unsigned char kind;
            double freq, gain, q;
            if (!r.U8(kind) || !r.F32(freq) || !r.F32(gain) || !r.F32(q))
                return models;
            HeadphoneFilterDef def;
            def.type = FilterTypeFromCode(kind);
            def.frequency = freq;
            def.gainDb = gain;
            def.q = q;
            m.filters.push_back(def);
        }
        models.push_back(std::move(m));
    }
    return models;
}

} // namespace

const std::vector<HeadphoneModelDef>& HeadphoneBank()
{
    // thread interface uniquement (l'éditeur et les tests y accèdent) ;
    // initialisation paresseuse une seule fois par processus.
    static const std::vector<HeadphoneModelDef> bank = DecodeBank();
    return bank;
}

const char* HeadphoneTypeLabel(int typeCode)
{
    switch (typeCode) {
    case 1: return "Écouteurs intra-aural";
    case 2: return "Écouteurs classiques";
    case 3: return "Casque gaming";
    default: return "Casque circum-aural";
    }
}

roomeq::preset::PresetDef HeadphonePreset(const HeadphoneModelDef& model)
{
    // miroir de HeadphonePresetLibrary.ToEqPreset (RoomEq.Core) :
    // mêmes clamps, mêmes types, même construction du nom.
    roomeq::preset::PresetDef preset;
    preset.name = model.brand + " " + model.model;
    preset.preampDb = std::clamp(model.preampDb, -15.0, 0.0);
    preset.bands.reserve(model.filters.size());
    for (const auto& f : model.filters) {
        roomeq::preset::EqBandDef band;
        band.type = f.type;
        band.frequency = std::clamp(f.frequency, 20.0, 20000.0);
        band.gainDb = std::clamp(f.gainDb, -24.0, 24.0);
        band.q = std::clamp(f.q, 0.1, 20.0);
        band.enabled = true;
        preset.bands.push_back(band);
    }
    return preset;
}

} // namespace roomeq::bank
