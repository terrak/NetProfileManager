#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Arrête et supprime le service Windows NetProfileManager.
#>
param(
    [string]$InstallDir = "C:\Program Files\NetProfileManager",
    [string]$DataDir = "C:\ProgramData\NetProfileManager"
)

$ErrorActionPreference = "Stop"
$ServiceName = "NetProfileManager"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Stop-Service -Name $ServiceName -Force
    & sc.exe delete $ServiceName | Out-Null
    Write-Host "Service $ServiceName supprimé."
}

if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
if (Test-Path $DataDir) { Remove-Item $DataDir -Recurse -Force }
Write-Host "Fichiers supprimés."
