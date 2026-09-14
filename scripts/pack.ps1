[CmdletBinding()]
param(
    [string]$SPTPath = "C:\HUH",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\BarrelHealing.Client\BarrelHealing.Client.csproj"

dotnet build $project -c $Configuration -p:SPTPath=$SPTPath
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dll = Join-Path $repo "src\BarrelHealing.Client\bin\$Configuration\BarrelHealing.Client.dll"
if (-not (Test-Path $dll)) { throw "Built, but no DLL at $dll" }

$destination = Join-Path $SPTPath "BepInEx\plugins\BarrelHealing"
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item $dll $destination -Force

Write-Host "Installed BarrelHealing.Client.dll to $destination"
