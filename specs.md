# Spécifications — NetProfileManager

Application Windows 11 en C# (.NET 8) permettant de mémoriser des **profils réseau**
(IP, masque, passerelle, DNS) et de les appliquer à une carte réseau, ou de remettre
une ou plusieurs cartes en **DHCP complet**, **sans demander le mot de passe
administrateur à l'exécution**.

## 1. Exigences fonctionnelles

### 1.1 Profils réseau
- Un profil contient : `Id` (GUID), `Name` (unique, obligatoire), `IpAddress` (IPv4),
  `SubnetMask` (IPv4, masque contigu validé), `Gateway` (IPv4, facultative),
  `DnsServers` (liste ordonnée d'IPv4, 0..n), `Description` (facultative).
- Opérations : **créer, modifier, supprimer, lister** (CRUD complet), depuis l'interface
  graphique et depuis le CLI.
- Validation à l'enregistrement : nom non vide et unique, IP/masque/passerelle/DNS
  syntaxiquement valides (IPv4 uniquement), masque contigu (ex. 255.255.255.0).
- Un profil **actif sur une carte ne peut pas être supprimé** : la suppression est
  refusée avec indication de la carte concernée.

### 1.2 Application d'un profil sur une carte réseau
- L'utilisateur choisit une carte et un profil, puis applique.
- L'application modifie la carte via `netsh interface ipv4` :
  - adresse statique : `set address name="<carte>" static <ip> <masque> [<passerelle> 1]` ;
  - DNS : `set dnsservers ... static <dns1> primary` puis `add dnsservers ... <dnsN> index=N`.
- **Règle d'exclusivité** : un profil statique ne peut être actif que sur **une seule
  carte à la fois**. Si on tente de l'appliquer ailleurs, l'opération est **refusée**
  et la réponse indique la carte qui l'héberge actuellement.
- Dans l'interface graphique, le conflit déclenche une boîte de dialogue : « le profil
  X est déjà actif sur la carte Y — quel réglage appliquer à la place sur Y ? »
  (autre profil ou DHCP). Après confirmation, le remplacement est appliqué sur Y **puis**
  le profil demandé sur la carte cible, dans cet ordre.
- En CLI, le conflit retourne le **code de sortie 3** et un message `CONFLIT:` indiquant
  la carte concernée, pour permettre au script de décider.

### 1.3 Mode DHCP complet
- Commande dédiée « DHCP » par carte : `set address ... dhcp` + `set dnsservers ... dhcp`
  (IP, passerelle **et** DNS repassent en automatique).
- **Le DHCP peut être activé simultanément sur plusieurs cartes** (aucune exclusivité).

### 1.4 Visibilité
- L'interface liste toutes les cartes réseau (hors loopback) avec : nom (alias),
  description, état opérationnel, IP actuelles, passerelle actuelle, DNS actuels,
  indicateur DHCP, et le **profil actuellement actif** (ou « DHCP », ou « aucun »).

## 2. Exigences non fonctionnelles

### 2.1 Exécution sans mot de passe administrateur
- Aucun UAC à chaque lancement. L'installation (unique) se fait avec un compte admin.
- Architecture : **service Windows** `NetProfileManager` tournant en compte
  **LocalSystem** (seul habilité à modifier la configuration IP) + **clients non élevés**
  (UI WPF et CLI) communiquant avec le service par un **named pipe local**
  `NetProfileManager.Pipe` (messages JSON sur une ligne, requête → réponse).
- ACL du pipe : `Everyone` en lecture/écriture (local uniquement), `SYSTEM` contrôle
  total. Aucune ouverture réseau.

### 2.2 Persistance
- `C:\ProgramData\NetProfileManager\profiles.json` : liste des profils.
- `C:\ProgramData\NetProfileManager\assignments.json` : carte → `Id de profil` ou
  marqueur `"DHCP"` (état géré uniquement par le service, de façon atomique via un
  verrou interne).
- ACL du dossier de données : `SYSTEM` et `Administrators` contrôle total, `Users`
  lecture seule.

### 2.3 Cible et performances
- Windows 11, .NET 8, C#. Interface **WPF** + **CLI console** scriptable.
- Réponse du service en quelques millisecondes hors application réseau ; application
  d'un profil en moins de quelques secondes (netsh).

## 3. Architecture logicielle

| Projet | Rôle |
|---|---|
| `NetProfileManager.Core` | Modèles (`NetworkProfile`, `AdapterInfo`, `AppState`), protocole du pipe (commandes, codes d'erreur, DTO), client pipe, stores JSON. |
| `NetProfileManager.Service` | Service Windows : serveur du named pipe, `CommandHandler` (règles métier + exclusivité), `NetworkApplier` (netsh), `AdapterEnumerator` (cartes). |
| `NetProfileManager.App` | WPF : fenêtre principale (cartes + profils), éditeur de profil, boîte de dialogue de conflit. |
| `NetProfileManager.Cli` | Console : `list-adapters`, `list-profiles`, `add-profile`, `update-profile`, `delete-profile`, `apply`, `dhcp`, `assignments`. |

## 4. Protocole du named pipe

Requête : `{"Command": "<commande>", "Payload": "<json échappé>"}` (une ligne).
Réponse : `{"Success": bool, "Message": string, "ErrorCode": string?, "Data": "<json>"}`.

Commandes : `list-adapters`, `list-profiles`, `save-profile`, `delete-profile`,
`apply-profile`, `set-dhcp`, `get-assignments`.

Codes d'erreur : `PROFILE_IN_USE` (Data = `{ProfileId, ProfileName, AdapterAlias}`),
`NOT_FOUND`, `INVALID`.

## 5. Installation / désinstallation

- `installer/Install-Service.ps1` (à exécuter **une fois en admin**) : copie les
  binaires dans `C:\Program Files\NetProfileManager`, crée et démarre le service
  (`start= auto`, `LocalSystem`), prépare les ACL de `C:\ProgramData\NetProfileManager`.
- `installer/Uninstall-Service.ps1` : arrête/supprime le service et les fichiers.
- Compilation : `dotnet publish` des 3 projets (voir README.md).

## 6. Sécurité

- Seul le service (SYSTEM) modifie la configuration réseau ; les clients ne transmettent
  que des commandes via le pipe local.
- Le pipe n'est pas exposé sur le réseau ; son ACL limite l'accès à la machine locale.
- Les profils et l'état sont en lecture seule pour les utilisateurs standards.

## 7. Limites connues

- IPv4 uniquement (pas d'IPv6).
- L'état « profil actif » reflète les applications faites via l'application ; une
  modification manuelle externe (netsh/Panneau de configuration) n'est pas détectée.
- La compilation n'a pas pu être vérifiée dans l'environnement de développement
  (bac à sable Linux sans SDK .NET) : à compiler sur Windows 11 avec le SDK .NET 8.
