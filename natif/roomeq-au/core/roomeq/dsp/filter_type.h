// RoomEq — type de filtre d'une bande (port exact de RoomEq.Core, spec I-AUD-07).
// L'ordre des énumérés correspond à RoomEq.Core.Dsp.FilterType (sérialisé en
// entiers dans certains fichiers, en chaînes dans les presets JSON).
#pragma once

namespace roomeq::dsp {

enum class FilterType {
    Peaking,
    LowShelf,
    HighShelf,
    LowPass,
    HighPass,
    Notch,
};

} // namespace roomeq::dsp
