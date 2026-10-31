// RoomEq — lecture des presets pour le cœur natif (spec I-AUD-07) : format
// JSON de l'application (énumérations en chaînes, champs PascalCase) et
// format binaire .ffp FabFilter Pro-Q 3 (module M-IO-06, écrit par
// FabFilterProQ3.cs — la lecture est l'opération inverse exacte).
#pragma once

#include "roomeq/preset/preset.h"

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>

namespace roomeq::preset {

/// Analyse un preset RoomEq au format JSON de l'application.
/// Tolérant : champs inconnus ignorés, camelCase accepté en plus du
/// PascalCase, `Enabled` par défaut vrai, type en chaîne ("Peaking"…) ou en
/// entier (ordre de l'énumération). Retourne false + message en cas d'erreur
/// de structure (bandes absentes, valeur impossible).
bool FromJson(std::string_view text, PresetDef& out, std::string& error);

/// Analyse un preset FabFilter Pro-Q 3 (.ffp, magie « FQ3p »).
/// Décode les 24 bandes (13 paramètres chacune) et le gain de sortie global
/// (→ préampli). Les coupes sont importées en pente 12 dB/oct (celle des
/// biquads RoomEq) ; BandPass/TiltShelf, sans équivalent, sont importés comme
/// des bandes Peaking (l'export RoomEq ne les produit jamais).
bool FromFfp(const uint8_t* data, size_t size, PresetDef& out, std::string& error);

/// Détecte le format d'octets de preset : "ffp" (binaire Pro-Q 3) ou "json".
/// Retourne une chaîne vide si inconnu.
std::string_view SniffFormat(const uint8_t* data, size_t size);

} // namespace roomeq::preset
