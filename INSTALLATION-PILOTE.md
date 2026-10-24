# Installation et signature du pilote « RoomEq Virtual Audio »

Ce document couvre l'installation du pilote de la carte son virtuelle RoomEq
(`roomeqvad`) et la **signature**, seule condition pour qu'un pilote Windows
10/11 64 bits soit accepté par le système. Deux voies sont prévues, au choix :

| Voie | Coût | Usage | Mise en place |
|---|---|---|---|
| **A. Signature de test** (intégrée aux scripts et à la CI) | Gratuit | Machines de test / développement | 5 minutes, ce document |
| **B. Attestation Microsoft** (officielle) | Certificat à signature EV (~300 €/an) + compte | Distribution au grand public | Ce document + Hardware Dev Center |

> **Pourquoi ?** Windows refuse par défaut tout pilote noyau non signé par
> Microsoft (politique de sécurité). Les produits équivalents du marché
> (câbles audio virtuels) sont signés pour cette même raison. Tant que le
> projet n'a pas de certificat EV, les builds officiels de la CI sont
> **signés-test**.

---

## 1. Installer le pilote sur une machine (application déjà installée)

1. Ouvrez RoomEq → onglet **Mixage & routage**.
2. Vérifiez que le protocole « **Carte virtuelle RoomEq** » est sélectionné.
3. Cliquez **« Installer le pilote RoomEq… »** et acceptez l'invite
   administrateur. Le paquet (`roomeqvad.inf` / `.sys` / `.cat`) est installé
   par `pnputil` — **rien n'est téléchargé**.
4. Les périphériques **« RoomEq Input »** et **« RoomEq Output »** apparaissent
   dans le panneau son (Windows les groupe sous « RoomEq Virtual Audio »).

Si l'installation réussit mais que **les périphériques n'apparaissent pas**,
le pilote est probablement signé-test et bloqué : passez à la voie A
ci-dessous. Le bouton « Supprimer le pilote » retire proprement le paquet.

---

## Voie A — Signature de test (gratuit, machines de test)

La CI produit déjà des pilotes signés-test avec un certificat dédié ; le
fichier `RoomEq-TestSigning.cer` est fourni **à côté** du pilote dans les
artefacts de build. Sur la machine cible (session administrateur) :

```bat
:: 1) installer le certificat de test dans les magasins de confiance
certutil -addstore Root RoomEq-TestSigning.cer
certutil -addstore TrustedPublisher RoomEq-TestSigning.cer

:: 2) activer le mode test de Windows (redémarrage requis)
bcdedit /set testsigning on
shutdown /r /t 0

:: 3) après redémarrage : installer le pilote
pnputil /add-driver roomeqvad.inf /install /scan
```

Après le redémarrage, un filigrane « Mode de test » apparaît en bas à droite
de l'écran : il signale simplement que Windows accepte les pilotes signés-test.

**Retirer le mode test** :

```bat
bcdedit /deletevalue testsigning
shutdown /r /t 0
```

### Signer soi-même un pilote compilé localement

`driver\roomeqvad\build\sign_driver_test.ps1` fait tout automatiquement
(certificat local, signature du `.sys` et du `.cat`, export du `.cer`) :

```powershell
powershell -ExecutionPolicy Bypass -File driver\roomeqvad\build\build_driver.ps1 -TestSign
```

---

## Voie B — Attestation Microsoft (distribution officielle)

L'attestation (« attestation signing ») fait signer le pilote **par Microsoft**
après contrôle automatique : les utilisateurs n'ont ni mode test, ni
certificat à installer. Étapes :

1. **Certificat à signature EV** : achetez un certificat de signature de code
   EV (DigiCert, Sectigo, GlobalSign… ~300 €/an, carte à puce ou HSM cloud
   selon le fournisseur). Il doit être au nom de la personne/l'entité qui
   publiera le pilote.
2. **Compte développeur matériel** : créez un compte Microsoft (Azure AD) et
   inscrivez-vous au programme matériels (Partner Center → Hardware
   Dev Center). L'inscription requiert le certificat EV pour prouver
   l'identité.
3. **Préparer l'envoi** :
   ```powershell
   # le dossier du paquet (roomeqvad.sys + .inf + .cat signé EV)
   MakeCab /f RoomEq.ddf          # ou l'archive CAB attendue par le portail
   signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 ^
             /f certificat-ev.pfx /p <motdepasse> roomeqvad.cab
   ```
4. **Soumettre** sur le portail (Hardware Dev Center → New hardware
   submission → pilote « attestation »). Le contrôle automatique prend
   généralement moins de 24 h.
5. **Récupérer** le `.cat` attesté signé par Microsoft, remplacer celui du
   paquet, republier l'installateur : le pilote s'installe alors sur
   n'importe quel Windows 10/11 64 bits **sans mode test**.

> Limites connues : l'attestation ne permet pas les pilotes avec composants
> tiers non signés et les comptes individuels peuvent être soumis à revue
> manuelle. Le pilote RoomEq (déclaré UNIVERSAL, sans dépendance exotique)
> est conforme au socle d'attestation.

---

## 3. Détails techniques (référence)

- **Pilote** : `roomeqvad.sys` — WDM/PortCls WaveRT (miniport WaveRT +
  topologies), dérivé de l'exemple SYSVAD du WDK (MIT, cf.
  `THIRD_PARTY_NOTICES.md`). Une paire d'endpoints : rendu « RoomEq Input »
  (Windows y joue), capture « RoomEq Output » (l'application lit), pont
  interne render→capture (`bridge.cpp`).
- **Format** : 48 kHz, stéréo, float 32 bits — le moteur audio Windows
  rééchantillonne tous les flux vers ce format.
- **INF** : installation par énumération racine (`ROOT\ROOMEQVAD`), service
  `roomeqvad` à la demande, fichiers dans le magasin de pilotes.
- **Désinstallation** : `pnputil /delete-driver oemNN.inf /uninstall /force`
  (le bouton « Supprimer le pilote » et l'installateur MSI le font).
- **Compilation** : `driver\roomeqvad\build\build_driver.ps1` — Visual Studio
  2022 (charge C++) ; le WDK 10 arrive par NuGet au premier build
  (~500 Mo, aucune extension VS à installer).
