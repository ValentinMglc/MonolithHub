param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'MonolithHub\MonolithHub.csproj'
$artifacts = Join-Path $root 'artifacts'
$appDir = Join-Path $artifacts 'app'

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
Write-Host "Monolith Hub $Version" -ForegroundColor Magenta

if (Test-Path $artifacts) { Remove-Item -Recurse -Force $artifacts }

dotnet publish $project -c Release -r win-x64 --self-contained true -o $appDir -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué" }
Get-ChildItem $appDir -Filter *.pdb | Remove-Item

Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath (Join-Path $artifacts "Monolith-Hub-$Version-portable.zip")

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 introuvable : winget install JRSoftware.InnoSetup" }

& $iscc /Q "/DAppVersion=$Version" "/DSourceDir=$appDir" (Join-Path $root 'installer\MonolithHub.iss')
if ($LASTEXITCODE -ne 0) { throw "La compilation de l'installateur a échoué" }

Get-ChildItem $artifacts -File | ForEach-Object { '{0,-36} {1,8:N1} Mo' -f $_.Name, ($_.Length / 1MB) }
