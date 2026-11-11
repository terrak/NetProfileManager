#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installe le service Windows NetProfileManager (compte SYSTEM) et prépare le dossier de données.
.DESCRIPTION
    À exécuter UNE FOIS avec un compte administrateur, après publication des binaires.
    L'application graphique et le CLI s'exécutent ensuite SANS élévation : ils parlent
    au service via un named pipe local.
.PARAMETER PublishDir
    Dossier contenant les binaires publiés (ex: sortie de 'dotnet publish').
.EXAMPLE
    .\Install-Service.ps1 -PublishDir "C:\build\publish"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir
)

$ErrorActionPreference = "Stop"

$InstallDir = "C:\Program Files\NetProfileManager"
$ServiceName = "NetProfileManager"
$DataDir = "C:\ProgramData\NetProfileManager"

if (-not (Test-Path "$PublishDir\NetProfileManager.Service.exe")) {
    throw "NetProfileManager.Service.exe introuvable dans $PublishDir"
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item "$PublishDir\*" -Destination $InstallDir -Recurse -Force

New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
icacls $DataDir /inheritance:r | Out-Null
icacls $DataDir /grant "SYSTEM:(OI)(CI)F" | Out-Null
icacls $DataDir /grant "Administrators:(OI)(CI)F" | Out-Null
icacls $DataDir /grant "Users:(OI)(CI)RX" | Out-Null

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

& sc.exe create $ServiceName binPath= "`"$InstallDir\NetProfileManager.Service.exe`"" start= auto obj= LocalSystem | Out-Null
& sc.exe start $ServiceName | Out-Null

Start-Sleep -Seconds 2
$service = Get-Service -Name $ServiceName
Write-Host "Service installé : $($service.Status). Binaires dans $InstallDir, données dans $DataDir."
Write-Host "Lancer ensuite NetProfileManager.exe (interface) ou NetProfileManager.Cli.exe (console) SANS droits admin."
