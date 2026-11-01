// RoomEq — sérialisation JSON d'un preset (v3.5.0 VST, partagée par les
// greffons AU et VST depuis la v3.6.0 — décision D23) : implémentation.
#include "roomeq/preset/preset_write.h"

#include <cmath>
#include <cstdio>
#include <string>

namespace roomeq::preset {
namespace {

const char* TypeName(roomeq::dsp::FilterType type)
{
    using roomeq::dsp::FilterType;
    switch (type) {
    case FilterType::LowShelf: return "LowShelf";
    case FilterType::HighShelf: return "HighShelf";
    case FilterType::LowPass: return "LowPass";
    case FilterType::HighPass: return "HighPass";
    case FilterType::Notch: return "Notch";
    case FilterType::Peaking: return "Peaking";
    }
    return "Peaking";
}

/// Nombre en %.17g (aller-retour exact double → texte → double).
void AppendNumber(std::string& out, double value)
{
    char buf[40];
    const int n = std::snprintf(buf, sizeof(buf), "%.17g", value);
    out.append(buf, static_cast<size_t>(n));
}

/// Chaîne JSON avec échappement minimal (guillemet, barre oblique inverse,
/// contrôles < 0x20 en \u00XX). Le format de preset de l'application ne
/// contient pas d'Unicode échappé : les octets UTF-8 passent tels quels.
void AppendString(std::string& out, const std::string& text)
{
    out.push_back('"');
    for (unsigned char c : text) {
        switch (c) {
        case '"': out += "\\\""; break;
        case '\\': out += "\\\\"; break;
        case '\b': out += "\\b"; break;
        case '\f': out += "\\f"; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default:
            if (c < 0x20) {
                char buf[8];
                std::snprintf(buf, sizeof(buf), "\\u%04x", c);
                out += buf;
            } else {
                out.push_back(static_cast<char>(c));
            }
        }
    }
    out.push_back('"');
}

} // namespace

std::string ToJson(const PresetDef& preset)
{
    std::string out = "{";
    out += "\"Name\":";
    AppendString(out, preset.name);
    out += ",\"PreampDb\":";
    AppendNumber(out, preset.preampDb);
    out += ",\"Bands\":[";
    for (size_t i = 0; i < preset.bands.size(); ++i) {
        const auto& b = preset.bands[i];
        if (i > 0)
            out.push_back(',');
        out += "{\"Type\":\"";
        out += TypeName(b.type);
        out += "\",\"Frequency\":";
        AppendNumber(out, b.frequency);
        out += ",\"GainDb\":";
        AppendNumber(out, b.gainDb);
        out += ",\"Q\":";
        AppendNumber(out, b.q);
        out += ",\"Enabled\":";
        out += b.enabled ? "true" : "false";
        out.push_back('}');
    }
    out += "]}";
    return out;
}

} // namespace roomeq::preset
