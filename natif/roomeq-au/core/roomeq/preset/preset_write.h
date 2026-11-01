// RoomEq — sérialisation JSON d'un preset (v3.5.0 VST, partagée par les
// greffons AU et VST depuis la v3.6.0 — décision D23). Le format est
// EXACTEMENT celui de l'application (PascalCase, énumérations en chaînes —
// JsonOpts.Default/JsonStringEnumConverter) : un preset édité dans un greffon
// est relisible par l'application et vice-versa.
#pragma once

#include "roomeq/preset/preset.h"

#include <string>

namespace roomeq::preset {

/// Sérialise un preset au format JSON de l'application (compact). Les noms
/// de bandes sont des chaînes sans caractères de contrôle — l'échappement
/// minimal (\" \\ et contrôles) est appliqué au nom du preset.
std::string ToJson(const PresetDef& preset);

} // namespace roomeq::preset
