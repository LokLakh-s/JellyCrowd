#Requires -Version 7
<#
  Build Jelly Crowd (Release) and deploy it into the local test Jellyfin's plugins folder, then restart
  the container so the plugin reloads. Run from anywhere — paths are resolved relative to this script.

  Usage:  pwsh ./deploy-plugin.ps1
#>
$ErrorActionPreference = 'Stop'

$stack   = $PSScriptRoot
$projDir = Resolve-Path (Join-Path $stack '..\Jellyfin.Plugin.JellyCrowd')
$csproj  = Join-Path $projDir 'Jellyfin.Plugin.JellyCrowd.csproj'
$binDir  = Join-Path $projDir 'bin\Release\net9.0'
$target  = Join-Path $stack 'jellyfin\config\plugins\JellyCrowd'

Write-Host '==> Building Jelly Crowd (Release)...' -ForegroundColor Cyan
$env:DOTNET_ROLL_FORWARD = 'Major'   # tolerate a newer installed .NET runtime
dotnet build $csproj -c Release

Write-Host "==> Deploying to $target" -ForegroundColor Cyan
if (Test-Path $target) { Remove-Item "$target\*" -Recurse -Force }
New-Item -ItemType Directory -Force $target | Out-Null
# Same DLL set the CI ships: the plugin + its bundled runtime deps (MailKit/MimeKit/BouncyCastle).
# The Jellyfin host assemblies are ExcludeAssets=runtime, so they are NOT in bin and won't be copied.
Copy-Item "$binDir\*.dll" $target -Force

# meta.json so Jellyfin registers the plugin. targetAbi is forced to 12.0.0.0 here so the RC server
# attempts to load it; the shipped build.yaml stays on 10.11 until the official compat bump. Name/guid/
# version are read live from build.yaml so they stay in sync.
$yaml = Get-Content (Join-Path $stack '..\build.yaml') -Raw   # build.yaml is in the JellyCrowd dir
function Get-YamlValue([string]$key) {
  if ($yaml -match "(?m)^$([regex]::Escape($key)):\s*`"?([^`"\r\n]+)`"?") { return $Matches[1].Trim() }
  return ''
}
$meta = [ordered]@{
  guid       = Get-YamlValue 'guid'
  name       = Get-YamlValue 'name'
  version    = Get-YamlValue 'version'
  targetAbi  = '12.0.0.0'
  framework  = 'net9.0'
  owner      = 'LokLakh-s'
  category   = 'General'
  overview   = 'Local Jellyfin 12 RC test build'
  assemblies = @('Jellyfin.Plugin.JellyCrowd.dll')
}
$meta | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $target 'meta.json') -Encoding utf8

Write-Host '==> Restarting Jellyfin container (if running)...' -ForegroundColor Cyan
try { docker restart jellycrowd-jellyfin | Out-Null } catch { Write-Host '   (container not running yet — start it with: docker compose up -d)' -ForegroundColor Yellow }

Write-Host 'Done.' -ForegroundColor Green
Write-Host 'Watch load/runtime errors with:  docker logs -f jellycrowd-jellyfin' -ForegroundColor Green
