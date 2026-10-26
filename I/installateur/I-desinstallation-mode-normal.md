# I-INS-06 — Désinstallation et retour au mode normal

- **Module** : M-INS-06 · **Version de spec** : 1 · **Code** : futur — vague 8
  (`src/RoomEq.Installer/UninstallSequence.cs`)
- **Référence scellée** : `roomeq-git-ref/installer/driver-setup/Program.cs`
  (`RunUninstall`, `RunModeNormal`, v2.2.2).

## Intention

Retirer proprement et COMPLÈTEMENT la carte virtuelle (périphérique, paquet
magasin, ASIO, certificat, fichiers temporaires) en best-effort — chaque
étape est tentée, aucune n'est bloquante, la désinstallation se termine
toujours par un état final cohérent et rapporté. Le retour au mode normal
est un chemin distinct qui coupe le mode test PUIS propose (sans jamais
imposer) le retrait du pilote devenu inutilisable.

## Contrat public

```csharp
namespace RoomEq.Installer;

public sealed class UninstallSequence(UiSink ui, ToolContract tools, Win32Pnp pnp)
{
    /// 5 étapes best-effort, jamais bloquantes.
    public SequenceResult Uninstall();

    /// Coupe le mode test puis PROPOSE le retrait complet (défaut OUI).
    public SequenceResult ModeNormal();
}
```

## Étapes de désinstallation (ordre IMMUABLE, chacune best-effort)

| n | Étape | Action |
|---|-------|--------|
| 1 | Périphérique `ROOT\ROOMEQVAD` | `DIF_REMOVE` (M-INS-02) ; introuvable = « rien à faire », pas une erreur |
| 2 | Paquet du magasin | résolution `oemNN.inf` (M-INS-04 : parse + repli) puis `pnputil /delete-driver <oem> /uninstall /force` ; introuvable = rien à faire |
| 3 | ASIO | suppression des clés registre + `C:\Program Files\RoomEq` (M-INS-07) |
| 4 | Certificat de test | retrait de Root + TrustedPublisher — par **empreinte** du `.cer` extrait, à défaut par **sujet** contenant « RoomEq Test Signing » (recherche dans Root) ; introuvable = rien à faire |
| 5 | Fichiers temporaires | suppression de `%TEMP%\RoomEqDriverSetup` |

Puis : si le mode test est actif, **proposition de le couper maintenant**
(défaut OUI → `DisableTestSigning` + proposition de reboot) ; refus =
consigne des trois sorties futures (bouton / `--mode-normal` / bcdedit direct).

## Retour au mode normal (`--mode-normal`)

1. Si le mode test n'est pas actif : `[SAUT]` + précision que le filigrane,
   s'il est visible, provient d'une autre source — aucune modification.
2. Sinon : `DisableTestSigning` (off puis deletevalue en secours) puis
   proposition de redémarrage immédiat (défaut OUI, `shutdown /r /t 5`).
3. Puis, si le pilote est encore présent (nœud OU magasin) : explication —
   « en mode normal, le pilote signé-test ne peut PAS se charger : la carte
   restera invisible tant que le pilote n'est pas signé Microsoft » — et
   **proposition** de retrait complet (défaut OUI → `Uninstall()`). Refus =
   « pilote laissé en place (invisible mais inoffensif) ».
4. Aucune trace du pilote → « rien d'autre à nettoyer ».

## Invariants

1. **Best-effort intégral** : une exception dans une étape = ligne `[!]` au
   journal, jamais un arrêt ; le code retour de la désinstallation est
   TOUJOURS 0 (les étapes critiques n'existent pas en retrait).
2. **Aucun retrait non consenti** : le retrait du pilote après retour au
   mode normal est proposé, jamais exécuté d'office.
3. Le certificat n'est retiré que s'il correspond à NOS magasins/actions
   (empreinte du `.cer` embarqué ou sujet « RoomEq Test Signing ») — jamais
   d'énumération-suppression large.
4. Le retrait du paquet magasin utilise le nom publié résolu (jamais le nom
   d'origine) ; l'absence de résolution n'est pas une erreur (déjà retiré).
5. Après désinstallation, l'utilisateur est informé de l'état du mode test
   restant et de comment le couper.

## Critères de test

- échec injecté à CHAQUE étape (1→5) : la suite continue, exit 0, chaque
  échec journalisé `[!]` ;
- périphérique introuvable à l'étape 1 → « rien à faire », pas d'exception ;
- certificat : retrait par empreinte quand le `.cer` est disponible ;
  repli par sujet quand il ne l'est pas ; introuvable → faux, pas d'erreur ;
- magasin : résolution oem réussie → arguments `/delete-driver oemNN.inf
  /uninstall /force` exacts ; résolution null → aucun appel pnputil ;
- mode-normal sans mode test actif → aucun bcdedit, aucun retrait proposé ;
- mode-normal avec pilote restant → question posée (défaut OUI) ; refus →
  aucun appel de retrait ;
- proposition de coupe du mode test après désinstallation : acceptée →
  `DisableTestSigning` appelé ; refusée → message des trois sorties.
