# Composants tiers — THIRD PARTY NOTICES

RoomEq s'appuie sur les bibliothèques open source suivantes, qui restent la
propriété de leurs auteurs respectifs et sont régies par leurs propres
licences (toutes permissives). Ce fichier n'a pas vocation à remplacer les
textes originaux, dont les versions complètes sont disponibles dans les
dépôts officiels indiqués.

## Composants d'exécution (runtime)

| Composant | Version | Licence | Auteurs / Copyright |
|---|---|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | 11.3.2 | MIT | Copyright (c) The AvaloniaUI Project |
| [Avalonia.Desktop](https://github.com/AvaloniaUI/Avalonia) | 11.3.2 | MIT | Copyright (c) The AvaloniaUI Project |
| [Avalonia.Themes.Fluent](https://github.com/AvaloniaUI/Avalonia) | 11.3.2 | MIT | Copyright (c) The AvaloniaUI Project |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.0 | MIT | Copyright (c) .NET Foundation and Contributors |
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | MIT | Copyright (c) 2020 Mark Heath & Contributors |

## Composants embarqués dans le pilote de la carte virtuelle

| Composant | Version | Licence | Auteurs / Copyright |
|---|---|---|---|
| [SYSVAD — Windows Driver Samples](https://github.com/microsoft/Windows-driver-samples/tree/main/audio/sysvad) | extrait 2025 (MIT) | MIT | Copyright (c) Microsoft Corporation |

Le pilote `roomeqvad.sys` (dossier `driver/roomeqvad/`) est **dérivé de
l'exemple SYSVAD** du Windows Driver Kit : mêmes fichiers d'origine
(`adapter.cpp`, `common.cpp`, `hw.cpp`, `kshelper.cpp`, `savedata.cpp`,
`ToneGenerator.cpp`, `basetopo.cpp`, `EndpointsCommon/`, topos micro),
élagués (périphériques sideband Bluetooth/USB, HDMI, SPDIF, matrices de micro
supprimés) et adaptés (pont interne render→capture `bridge.cpp`, formats
48 kHz/float, noms et INF RoomEq). Les en-têtes d'origine, qui mentionnent
Microsoft, sont conservés.

## Données intégrées : corrections casques/écouteurs (AutoEq)

| Composant | Version | Licence | Auteurs / Copyright |
|---|---|---|---|
| [AutoEq](https://github.com/jaakkopasanen/AutoEq) | résultats du master (MIT) | MIT | Copyright (c) 2018-2025 Jaakko Pasanen et contributeurs |

La bibliothèque de corrections casques (`src/RoomEq.Core/Headphones/
headphone_presets.json`, ressource intégrée) contient des corrections
paramétriques **dérivées des résultats publics du projet AutoEq** (Jaakko
Pasanen, licence MIT), eux-mêmes calculés à partir de mesures de têtes
artificielles publiées par leurs auteurs respectifs — principalement
**oratory1990**, **crinacle**, **Super Review**, **Rtings** et
**HypetheSonics**. Chaque preset embarque la mention de sa source de mesure
(champ `source`). Aucune dépendance d'exécution : les données sont figées à la
compilation (génération par `scripts/fetch_headphone_presets.py`, côté
environnement de développement).

## Composants de test (ne sont pas embarqués dans l'application)

| Composant | Version | Licence | Auteurs / Copyright |
|---|---|---|---|
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | 17.11.1 | MIT | Copyright (c) Microsoft Corporation |
| [xunit](https://github.com/xunit/xunit) | 2.9.2 | Apache 2.0 | Copyright (c) .NET Foundation and Contributors |
| [xunit.runner.visualstudio](https://github.com/xunit/visualstudio.xunit) | 2.8.2 | Apache 2.0 | Copyright (c) .NET Foundation and Contributors |

## Texte de la licence MIT (composants listés ci-dessus)

> Permission is hereby granted, free of charge, to any person obtaining a
> copy of this software and associated documentation files (the "Software"),
> to deal in the Software without restriction, including without limitation
> the rights to use, copy, modify, merge, publish, distribute, sublicense,
> and/or sell copies of the Software, and to permit persons to whom the
> Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in
> all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
> THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
> FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
> DEALINGS IN THE SOFTWARE.

## Remerciements

Merci aux équipes Avalonia, .NET Foundation, Mark Heath (NAudio) et à la
communauté .NET : RoomEq ne serait pas possible sans leur travail.
