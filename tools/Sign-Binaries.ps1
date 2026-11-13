<#
.SYNOPSIS
    Signe les executables publies avec un certificat Authenticode (contre les faux positifs antivirus).

.DESCRIPTION
    Deux modes :

    - Certificat interne AD CS (recommande en entreprise) : demandez un certificat
      « Code Signing » (EKU 1.3.6.1.5.5.7.3.3) aupres de votre PKI interne, installez-le
      dans le magasin Personnel de la machine de build, puis signez avec -UseCertStore.
      Si la racine AD CS est deployee sur les postes du domaine (Trusted Root) et le
      certificat editeur dans Trusted Publishers (GPO), tous les postes du groupe
      reconnaissent le programme comme provenant d'un editeur approuve.

    - Fichier .pfx (AC publique type DigiCert/Sectigo, ou Azure Trusted Signing) :
      signez avec -PfxPath.

    Dans les deux cas, un horodatage RFC 3161 (DigiCert) est applique : les signatures
    restent valides apres expiration du certificat.

.PARAMETER UseCertStore
    Utilise le meilleur certificat « Code Signing » du magasin Windows (signtool /a),
    ou celui dont le sujet correspond a -CertSubject (signtool /n).

.PARAMETER CertSubject
    Sous-jet (CN) du certificat a utiliser, ex. "Contoso Code Signing". Sans cette option
    avec -UseCertStore, signtool choisit automatiquement le certificat Code Signing valide.

.PARAMETER PfxPath
    Chemin d'un certificat .pfx (mode fichier, alternatif a -UseCertStore).

.PARAMETER PfxPassword
    Mot de passe du .pfx (sinon le .pfx doit etre sans mot de passe ou interactive).

.PARAMETER PublishDir
    Dossier contenant les binaires publies (sortie de 'dotnet publish -o publish').

.EXAMPLE
    .\tools\Sign-Binaries.ps1 -UseCertStore -PublishDir ".\publish"

.EXAMPLE
    .\tools\Sign-Binaries.ps1 -PfxPath "C:\certs\NetProfileManager.pfx" -PublishDir ".\publish"
#>
param(
    [switch]$UseCertStore,

    [string]$CertSubject,

    [string]$PfxPath,

    [string]$PfxPassword,

    [string]$PublishDir = "$PSScriptRoot\..\publish"
)

$ErrorActionPreference = "Stop"

if ($UseCertStore -and $PfxPath) { throw "Choisissez un seul mode : -UseCertStore OU -PfxPath." }
if (-not $UseCertStore -and -not $PfxPath) { throw "Indiquez -UseCertStore (certificat AD CS / magasin Windows) ou -PfxPath (fichier .pfx)." }
if ($PfxPath -and -not (Test-Path $PfxPath)) { throw "Certificat introuvable : $PfxPath" }

$PublishDir = (Resolve-Path $PublishDir).Path

# Localise signtool.exe dans le Windows SDK
$kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$signtool = Get-ChildItem -Path $kitsRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'x64' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw "signtool.exe introuvable dans $kitsRoot (installer le Windows SDK)." }

$signArgs = @("sign", "/fd", "SHA256", "/tr", "http://timestamp.digicert.com", "/td", "SHA256")
if ($UseCertStore) {
    if ($CertSubject) { $signArgs += @("/n", $CertSubject) } else { $signArgs += "/a" }
} else {
    $signArgs += @("/f", $PfxPath)
    if ($PfxPassword) { $signArgs += @("/p", $PfxPassword) }
}

Get-ChildItem -Path $PublishDir -Filter *.exe | ForEach-Object {
    Write-Host "Signature de $($_.Name)..."
    & $signtool @signArgs $_.FullName
    if ($LASTEXITCODE -ne 0) { throw "Echec de signature pour $($_.Name) (code $LASTEXITCODE)." }
    & $signtool verify /pa /v $_.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Verification de la signature echouee pour $($_.Name)." }
}

Write-Host "Binaires signes et verifies : $PublishDir"
