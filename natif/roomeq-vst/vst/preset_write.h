// RoomEq VST — sérialisation JSON d'un preset (v3.5.0). Le cœur AU est en
// lecture seule (scellé) : l'écriture vit dans la couche VST. Le format est
// EXACTEMENT celui de l'application (PascalCase, énumérations en chaînes —
// JsonOpts.Default/JsonStringEnumConverter) : un preset édité dans le greffon
// est relisible par l'application et vice-versa.
#pragma once

#include "roomeq/preset/preset.h"

#include <string>

namespace roomeq_vst {

/// Sérialise un preset au format JSON de l'application (compact). Les noms
/// de bandes sont des chaînes sans caractères de contrôle — l'échappement
/// minimal (\" \\ et contrôles) est appliqué au nom du preset.
std::string PresetToJson(const roomeq::preset::PresetDef& preset);

} // namespace roomeq_vst
