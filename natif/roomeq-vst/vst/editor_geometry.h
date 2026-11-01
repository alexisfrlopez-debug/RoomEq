// RoomEq VST — géométrie de l'éditeur (v3.5.1) : source de vérité UNIQUE.
//
// Retour utilisateur réel v3.5.0 : « l'interface n'est pas visible
// entièrement, certaines parties ne sont pas accessibles ». Cause : le rect
// déclaré à l'hôte (kEffEditGetRect, RoomEqVst.cpp) annonçait encore
// 520×240 — la taille d'avant la rangée casques — alors que la fenêtre
// créée par l'éditeur (EditorWin32.cpp) faisait déjà 560×320. Le DAW
// dimensionne sa fenêtre sur le rect déclaré : les 80 derniers pixels de
// hauteur (bouton « Charger… », listes déroulantes casque, « Appliquer »)
// et les 40 derniers de large (VU-mètre, « Appliquer ») étaient coupés.
//
// Ce header lie structurellement les deux : l'éditeur crée sa fenêtre avec
// ces constantes ET le rect déclaré à l'hôte est construit avec les mêmes.
// Un changement de l'un sans l'autre ne compile plus (static_assert) ou
// casse le test TestEditorRectGeometry — plus jamais de dérive silencieuse.
#pragma once

#include "vst2_abi.h"

#include <cstdint>

namespace roomeq_editor {

/// Largeur de la fenêtre éditeur (px logiques, 96 dpi).
constexpr int kEditorWidth = 560;
/// Hauteur de la fenêtre éditeur (px logiques, 96 dpi).
constexpr int kEditorHeight = 320;

/// Rect VST déclaré à l'hôte (kEffEditGetRect). Champs dans l'ordre de
/// l'ABI : top, left, bottom, right — la hauteur/largeur réelles de la
/// fenêtre créée par VstEditorOpen.
constexpr vst::ERect EditorRect()
{
    return { 0, 0, static_cast<int16_t>(kEditorHeight),
        static_cast<int16_t>(kEditorWidth) };
}

static_assert(kEditorWidth > 0 && kEditorHeight > 0,
    "l'éditeur doit avoir une taille positive");
static_assert(EditorRect().bottom == kEditorHeight
        && EditorRect().right == kEditorWidth,
    "le rect déclaré doit couvrir exactement la fenêtre de l'éditeur");
static_assert(kEditorWidth <= 32767 && kEditorHeight <= 32767,
    "les champs ERect sont des entiers 16 bits");

} // namespace roomeq_editor
