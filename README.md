# NetProfileManager

Application Windows 11 (.NET 8, C#) pour mémoriser des **profils réseau** (IP, masque,
passerelle, DNS) et les appliquer à une carte réseau, ou remettre une ou plusieurs
cartes en **DHCP complet** (IP + passerelle + DNS automatiques).

## Téléchargement

**Version 1.0.0** — Windows 64 bits (.NET 8, autonome) :

[NetProfileManager-v1.0.0-win-x64.zip](https://github.com/terrak/NetProfileManager/raw/v1.0.0/releases/v1.0.0/NetProfileManager-v1.0.0-win-x64.zip) (~1 Mo)

Le zip contient les trois programmes compilés (`NetProfileManager.exe`, `NetProfileManager.Cli.exe`,
`NetProfileManager.Service.exe`) ainsi que les scripts d'installation. Après extraction, installer
le service **une seule fois avec un compte administrateur** :

```powershell
.\NetProfileManager-v1.0.0\installer\Install-Service.ps1 -PublishDir "C:\chemin\extrait\NetProfileManager-v1.0.0\NetProfileManager"
```

Ensuite, lancer `NetProfileManager.exe` (interface graphique) ou `NetProfileManager.Cli.exe` (console)
**sans aucun droit administrateur**.

## Règles métier

- Un **profil statique ne peut être actif que sur une seule carte réseau** à la fois.
  Si vous l'appliquez à une autre carte, l'application annonce le conflit et vous
  demande quel réglage (autre profil ou DHCP) appliquer à la place sur l'autre carte.
- Le mode **DHCP peut être activé sur autant de cartes que souhaité**.
- L'application s'exécute **sans mot de passe administrateur** : aucun UAC à chaque
  lancement.

## Architecture (pourquoi aucun mot de passe admin à l'exécution)

```
┌────────────────────────────┐         named pipe local          ┌──────────────────────────┐
│  NetProfileManager.exe     │  (JSON, pipe                     │  Service Windows         │
│  (WPF, utilisateur std)   │   "NetProfileManager.Pipe")       │  NetProfileManager       │
│  ou NetProfileManager.Cli  │ ────────────────────────────────► │  (compte SYSTEM)         │
└────────────────────────────┘                                   │  applique via netsh      │
                                                                 └──────────────────────────┘
                                                        C:\ProgramData\NetProfileManager\
                                                        ├── profiles.json      (profils)
                                                        └── assignments.json   (profil actif / carte)
```

- Le **service** tourne en `LocalSystem`, c'est lui seul qui modifie la configuration
  réseau (`netsh interface ipv4 ...`). Il est installé **une fois** par un compte admin.
- L'**interface graphique** et le **CLI** tournent en utilisateur standard et passent
  toutes les commandes par le named pipe (ACL : lecture/écriture pour Everyone en local).
- L'**exclusivité des profils** est appliquée par le service, de façon atomique.

## Projets

| Projet | Rôle |
|---|---|
| `src/NetProfileManager.Core` | Modèles, protocole du pipe, client pipe, stores JSON |
| `src/NetProfileManager.Service` | Service Windows (pipe + netsh + règles métier) |
| `src/NetProfileManager.App` | Interface WPF (`NetProfileManager.exe`) |
| `src/NetProfileManager.Cli` | Mode console scriptable (`NetProfileManager.Cli.exe`) |

## Compilation

Sur une machine Windows 11 avec le SDK .NET 8 :

```powershell
dotnet publish src/NetProfileManager.Service -c Release -o publish
dotnet publish src/NetProfileManager.App     -c Release -o publish
dotnet publish src/NetProfileManager.Cli     -c Release -o publish
```

## Installation (une fois, compte administrateur)

```powershell
.\installer\Install-Service.ps1 -PublishDir "C:\chemin\vers\publish"
```

Le script copie les binaires dans `C:\Program Files\NetProfileManager`, crée le
service Windows `NetProfileManager` (démarrage auto, compte SYSTEM), le démarre,
et prépare `C:\ProgramData\NetProfileManager` (SYSTEM : contrôle total, Users : lecture).

Désinstallation : `.\installer\Uninstall-Service.ps1`

## Utilisation

### Interface graphique (aucun droit admin requis)

Lancer `NetProfileManager.exe` :

- liste des **cartes réseau** avec leur configuration actuelle et le profil actif ;
- pour chaque carte : choisir un profil (ou « DHCP (automatique) ») puis **Appliquer** ;
- gestion des **profils** : Ajouter / Modifier / Supprimer (IP, masque, passerelle
  facultative, DNS primaire/secondaires).

### Console (scriptable, aucun droit admin requis)

```powershell
NetProfileManager.Cli.exe list-adapters
NetProfileManager.Cli.exe list-profiles
NetProfileManager.Cli.exe add-profile --name "Bureau" --ip 192.168.10.50 --mask 255.255.255.0 --gateway 192.168.10.1 --dns 192.168.10.1,8.8.8.8
NetProfileManager.Cli.exe update-profile --id <ID> --dns 1.1.1.1
NetProfileManager.Cli.exe delete-profile --id <ID>
NetProfileManager.Cli.exe apply --adapter "Ethernet" --profile "Bureau"
NetProfileManager.Cli.exe dhcp --adapter "Wi-Fi"
NetProfileManager.Cli.exe assignments
```

En cas de conflit (profil déjà actif sur une autre carte), `apply` retourne le code
de sortie `3` et affiche la carte concernée dans le message d'erreur.

## Sécurité

- Le named pipe est local à la machine ; son ACL autorise les utilisateurs standards
  à invoquer le service, rien d'autre. Aucune ouverture réseau.
- Seul le service (SYSTEM) peut modifier la configuration IP des cartes.

Voir `specs.md` pour l'ensemble des spécifications.
