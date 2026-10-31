// RoomEq — lecture des presets (spec I-AUD-07) : JSON de l'application.
#include "roomeq/preset/preset_io.h"

#include "roomeq/json/mini_json.h"

#include <cmath>

namespace roomeq::preset {

namespace {

using json::Value;

/// Extrait un nombre d'une valeur JSON, avec repli entre deux clés
/// (PascalCase de l'application puis camelCase toléré).
double NumberOf(const Value& object, std::string_view pascal, std::string_view camel,
    double fallback)
{
    const Value& v = object.Get(pascal);
    if (v.IsNumber())
        return v.AsNumber();
    if (!camel.empty()) {
        const Value& v2 = object.Get(camel);
        if (v2.IsNumber())
            return v2.AsNumber();
    }
    return fallback;
}

bool BoolOf(const Value& object, std::string_view pascal, std::string_view camel,
    bool fallback)
{
    const Value& v = object.Get(pascal);
    if (v.IsBool())
        return v.AsBool();
    if (!camel.empty()) {
        const Value& v2 = object.Get(camel);
        if (v2.IsBool())
            return v2.AsBool();
    }
    return fallback;
}

dsp::FilterType TypeOf(const Value& band)
{
    const Value& v = band.Get("Type").kind() != Value::Kind::Null
        ? band.Get("Type")
        : band.Get("type");
    if (v.IsString()) {
        const std::string& s = v.AsString();
        if (s == "LowShelf") return dsp::FilterType::LowShelf;
        if (s == "HighShelf") return dsp::FilterType::HighShelf;
        if (s == "LowPass") return dsp::FilterType::LowPass;
        if (s == "HighPass") return dsp::FilterType::HighPass;
        if (s == "Notch") return dsp::FilterType::Notch;
        return dsp::FilterType::Peaking; // inconnu → transparent
    }
    if (v.IsNumber()) {
        switch (static_cast<int>(v.AsNumber(-1))) {
        case 1: return dsp::FilterType::LowShelf;
        case 2: return dsp::FilterType::HighShelf;
        case 3: return dsp::FilterType::LowPass;
        case 4: return dsp::FilterType::HighPass;
        case 5: return dsp::FilterType::Notch;
        default: return dsp::FilterType::Peaking;
        }
    }
    return dsp::FilterType::Peaking;
}

/// Bornes du modèle RoomEq : le clamp définitif se fait dans BiquadFilter::
/// Configure (comme dans l'application), ici on écarte seulement les valeurs
/// impossibles (NaN/inf) pour ne pas polluer les filtres.
bool Finite(double v) { return std::isfinite(v); }

} // namespace

bool FromJson(std::string_view text, PresetDef& out, std::string& error)
{
    Value root;
    if (!json::Parse(text, root, error))
        return false;
    if (!root.IsObject())
        return (error = "un objet preset est attendu", false);

    out = PresetDef{};

    const Value& name = root.Get("Name").kind() != Value::Kind::Null
        ? root.Get("Name")
        : root.Get("name");
    if (name.IsString())
        out.name = name.AsString();

    out.preampDb = NumberOf(root, "PreampDb", "preampDb", 0.0);

    const Value& bands = root.Get("Bands").kind() != Value::Kind::Null
        ? root.Get("Bands")
        : root.Get("bands");
    if (bands.IsNull())
        return true; // preset sans bandes : transparent, valide
    if (!bands.IsArray())
        return (error = "« Bands » doit être un tableau", false);

    out.bands.reserve(bands.Size());
    for (size_t i = 0; i < bands.Size(); ++i) {
        const Value& band = bands.At(i);
        if (!band.IsObject())
            return (error = "une bande doit être un objet", false);

        EqBandDef def;
        def.type = TypeOf(band);
        def.frequency = NumberOf(band, "Frequency", "frequency", 1000.0);
        def.gainDb = NumberOf(band, "GainDb", "gainDb", 0.0);
        def.q = NumberOf(band, "Q", "q", 1.1);
        def.enabled = BoolOf(band, "Enabled", "enabled", true);

        if (!Finite(def.frequency) || !Finite(def.gainDb) || !Finite(def.q))
            return (error = "valeur non finie dans une bande", false);

        out.bands.push_back(def);
    }
    return true;
}

std::string_view SniffFormat(const uint8_t* data, size_t size)
{
    if (data != nullptr && size >= 4 && data[0] == 'F' && data[1] == 'Q'
        && data[2] == '3' && data[3] == 'p')
        return "ffp";
    // JSON : ignorer un éventuel BOM UTF-8 puis les espaces de tête
    size_t i = 0;
    if (data != nullptr && size >= 3 && data[0] == 0xEF && data[1] == 0xBB
        && data[2] == 0xBF)
        i = 3;
    while (data != nullptr && i < size
           && (data[i] == ' ' || data[i] == '\t' || data[i] == '\r' || data[i] == '\n'))
        ++i;
    if (data != nullptr && i < size && data[i] == '{')
        return "json";
    return {};
}

} // namespace roomeq::preset
