# RoomEq APO — égaliseur système sans pilote (N-APO-01 / I-AUD-09)

`RoomEqApo.dll` est un **Audio Processing Object** : un objet COM user-mode
que le moteur audio de Windows (`audiodg.exe`) charge sur les sorties où
l'application l'a inscrit. Tout le son de ces sorties traverse l'égaliseur
RoomEq **de l'intérieur** du moteur audio — zéro pilote noyau, zéro signature
Microsoft, zéro mode test, zéro dépendance tierce, **latence ajoutée nulle**
(décision D24, analyse complète : `docs/EQ-SYSTEME-APO.md`).

## Ce que fait l'APO

- applique la courbe active (`%ProgramData%\RoomEq\apo\active.json`, format
  preset de l'application — cœur DSP D23 partagé avec VST/AU) aux voies
  gauche/droite ; les autres canaux (centre, LFE, surround…) passent intacts ;
- recharge la courbe en arrière-plan (sondage 100 ms, horodatage fin) et
  **fusionne les changements par fondu enchaîné de 20 ms** : le son ne
  s'interrompt jamais, même en plein glissement de curseur ;
- dégrade TOUT imprévu en pass-through transparent (config absente, JSON
  corrompu, format inattendu) : le son passe, non égalisé ;
- ne retourne jamais d'échec aux appels du moteur audio (au-delà de 10
  échecs, Windows désactiverait les effets système du périphérique) ;
- journalise son activité dans `%ProgramData%\RoomEq\apo\apo.log`.

## Architecture

```
audiodg.exe (moteur audio Windows, LocalService)
  └─ RoomEqApo.dll (COM, CLSID {EAA24610-59C6-4591-A23E-9D0A2C4E1A15})
       ├─ thread RT : APOProcess — aucun lock, aucune allocation, aucune E/S
       │    (swap de chaîne par pointeur atomique, cimetière pour les
       │     libérations mémoire, fondu enchaîné linéaire)
       └─ thread de fond : sonde active.json, reconstruit la chaîne EqChain,
            publie par pointeur atomique, consigne dans apo.log
```

Le cœur (`apo_abi.h`) déclare l'ABI COM APO en auto-contenu : les mêmes
sources compilent (1) en DLL Windows via Zig/MinGW et (2) dans le harnais de
tests Linux (`roomeq_apo_tests`, lié directement). Les IIDs/structures sont
vérifiés contre le SDK 10.0.10240 et le comportement d'Equalizer APO.

**Agrégation COM (v3.9.9)** : audiodg instancie TOUS les sAPO avec un
contrôleur d'agrégation (`pUnkOuter` non nul + `IID_IUnknown`) — la refuser
coupe la sortie (retour terrain v3.9.8 : 0x80040110). L'objet implémente le
pattern complet : `INonDelegatingUnknown` (vtable binairement compatible
IUnknown, technique Equalizer APO), IUnknown déléguant au contrôleur,
identité interne non déléguante seule détentrice de la durée de vie, fabrique
acceptant `CreateInstance(pUnkOuter, IID_IUnknown)` — verrouillé par la
suite d'agrégation du harnais (faux contrôleur à compteurs).

## Construire / tester

```bash
# 1) harnais de tests natifs (Linux, g++ du socle — aucun sudo)
natif/roomeq-apo/build-tests.sh      # → 126 vérifications, 0 échec attendu

# 2) DLL de livraison Windows (x64 + ARM64, Zig 0.14.1)
natif/roomeq-apo/build-apo.sh        # → dist/RoomEqApo_{x64,arm64}.dll + zip
```

## Ce qui n'est PAS géré par l'APO (limites honnêtes)

- les flux **WASAPI exclusif / ASIO / Kernel Streaming** contournent le
  moteur audio (comme ils contournent tout effet système) ; les DAW ont le
  pont ASIO intégré de RoomEq ;
- les effets EFX du fabricant (Realtek, Dolby…) sont suspendus sur une
  sortie activée — sauvegardés au registre, restaurés à la désactivation ;
- une mise à jour de pilote audio peut écraser l'inscription →
  l'application la répare automatiquement (au démarrage et à chaque
  événement « périphériques modifiés ») ;
- position EFX seule, inscription par endpoint render : l'activation est
  **par sortie** (c'est aussi un avantage — courbe par périphérique).

L'inscription au registre, l'élévation UAC, la sauvegarde/restauration et
l'auto-réparation vivent côté application :
`src/RoomEq.Audio/SystemApo/` (registrar testé sur faux registre en CI).
