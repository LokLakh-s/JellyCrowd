#Requires -Version 7
<#
  Build Jelly Crowd (Release) once and deploy the SAME build into BOTH local test instances — Jellyfin 12
  and Jellyfin 10.11 — then restart them so the plugin reloads. Run from anywhere; paths are resolved
  relative to this script. Mirror of deploy-plugin.sh, for a Windows host with the .NET SDK installed.

  Needs BOTH the .NET 9 and .NET 10 SDKs on the host: the plugin and its 10.11 companion are net9.0, the
  Jellyfin 12 companion is net10.0 (Jellyfin.Controller 12.x ships lib/net10.0 only).

  Usage:  pwsh ./deploy-plugin.ps1
#>
$ErrorActionPreference = 'Stop'

$stack   = $PSScriptRoot
$projDir = Resolve-Path (Join-Path $stack '..\Jellyfin.Plugin.JellyCrowd')
$csproj  = Join-Path $projDir 'Jellyfin.Plugin.JellyCrowd.csproj'
$binDir  = Join-Path $projDir 'bin\Release\net9.0'

Write-Host '==> Building Jelly Crowd (Release)...' -ForegroundColor Cyan
$env:DOTNET_ROLL_FORWARD = 'Major'   # tolerate a newer installed .NET runtime
dotnet build $csproj -c Release

# Name/guid/version are read live from build.yaml so they stay in sync with what is shipped.
$yaml = Get-Content (Join-Path $stack '..\build.yaml') -Raw   # build.yaml is in the JellyCrowd dir
function Get-YamlValue([string]$key) {
  if ($yaml -match "(?m)^$([regex]::Escape($key)):\s*`"?([^`"\r\n]+)`"?") { return $Matches[1].Trim() }
  return ''
}

function Deploy-Instance([string]$target, [string]$container) {
  Write-Host "==> Deploying to $target" -ForegroundColor Cyan
  if (Test-Path $target) { Remove-Item $target -Recurse -Force }
  New-Item -ItemType Directory -Force $target | Out-Null

  # Same DLL set the CI ships: the plugin + its bundled runtime deps (MailKit/MimeKit/BouncyCastle).
  # The Jellyfin host assemblies are ExcludeAssets=runtime, so they are NOT in bin and won't be copied.
  Copy-Item "$binDir\*.dll" $target -Force

  # Isolated Skip Outro companion, as lib/*.dll.bin — BOTH halves (10.11-built and 12-built); the plugin
  # loads whichever one the running host can. Jellyfin loads every "*.dll" under the plugin folder and each
  # half throws on the other's major, which would disable the whole plugin; the allowlist is rewritten to []
  # by a manifest install and the walk is recursive, so only the extension keeps them out.
  New-Item -ItemType Directory -Force (Join-Path $target 'lib') | Out-Null
  Copy-Item "$binDir\lib\*.dll.bin" (Join-Path $target 'lib') -Force

  # targetAbi stays 10.11.0.0 — the stamp the published manifest carries. A server accepts any targetAbi at
  # or below its own version, so one artifact covers 10.11 and 12 alike, and stamping the dev 12 instance
  # any other way would test a package we never ship.
  # "assemblies" is [] because that is what Jellyfin itself writes when installing from a repository
  # manifest; deploying with an allowlist would make the dev stack friendlier than production and hide a
  # disabled plugin. Set JC_DEV_ALLOWLIST=1 to keep the allowlist for comparison.
  $assemblies = if ($env:JC_DEV_ALLOWLIST -eq '1') { @('Jellyfin.Plugin.JellyCrowd.dll') } else { @() }
  $meta = [ordered]@{
    guid       = Get-YamlValue 'guid'
    name       = Get-YamlValue 'name'
    version    = Get-YamlValue 'version'
    targetAbi  = '10.11.0.0'
    framework  = 'net9.0'
    owner      = 'LokLakh-s'
    category   = 'General'
    overview   = 'Local test build'
    assemblies = $assemblies
  }
  # -Depth matters: an empty array must still serialize as [], not collapse.
  $meta | ConvertTo-Json -Depth 4 -AsArray:$false | Set-Content (Join-Path $target 'meta.json') -Encoding utf8

  try { docker restart $container | Out-Null }
  catch { Write-Host "   ($container not running yet — start it with: docker compose up -d)" -ForegroundColor Yellow }
}

Deploy-Instance (Join-Path $stack 'jellyfin\config\plugins\JellyCrowd')      'jellycrowd-jellyfin'
Deploy-Instance (Join-Path $stack 'jellyfin-1011\config\plugins\JellyCrowd') 'jellycrowd-jellyfin-1011'

Write-Host 'Done.' -ForegroundColor Green
Write-Host '  12.1  : http://localhost:8096   (logs: docker logs -f jellycrowd-jellyfin)' -ForegroundColor Green
Write-Host '  10.11 : http://localhost:8097   (logs: docker logs -f jellycrowd-jellyfin-1011)' -ForegroundColor Green
