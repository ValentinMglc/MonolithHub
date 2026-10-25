param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$OutFile
)

$ErrorActionPreference = 'Stop'
$changelog = Get-Content (Join-Path $PSScriptRoot '..\CHANGELOG.md') -Raw -Encoding UTF8

$match = [regex]::Match($changelog, "(?ms)^## \[$([regex]::Escape($Version))\][^\n]*\n(.*?)(?=^## \[|\z)")
if (-not $match.Success) { throw "Version $Version absente de CHANGELOG.md" }

$footer = @"
---
**Installation** : téléchargez ``Monolith-Hub-Setup-$Version.exe`` ci-dessous (ou la version portable ``.zip``).
La température du CPU nécessite le pilote gratuit [PawnIO](https://pawnio.eu). Monolith Hub n'est pas affilié à HP Inc.
"@
$notes = $match.Groups[1].Value.Trim() + "`n`n" + $footer

New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
Set-Content -Path $OutFile -Value $notes -Encoding UTF8
Write-Host $notes
