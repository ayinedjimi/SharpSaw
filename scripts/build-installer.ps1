<#
.SYNOPSIS
  Construit l'installateur Inno Setup (dist\SharpSaw-Setup-<version>.exe).
  Prérequis : Inno Setup 6, et dist\SharpSaw.exe déjà construit par scripts\build.ps1.
#>
param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 introuvable (https://jrsoftware.org/isinfo.php)' }
if (-not (Test-Path (Join-Path $root 'dist\SharpSaw.exe'))) { throw 'dist\SharpSaw.exe absent : lancez d''abord scripts\build.ps1' }

# Runtime Visual C++ requis par les DLL natives (signé Microsoft)
$vc = Join-Path $root 'installer\vc_redist.x64.exe'
if (-not (Test-Path $vc)) { Invoke-WebRequest 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $vc }
$sig = Get-AuthenticodeSignature $vc
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'Signature de vc_redist.x64.exe invalide' }

& $iscc "/DAppVersion=$Version" (Join-Path $root 'installer\SharpSaw.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC a échoué ($LASTEXITCODE)" }
Write-Host "OK : $(Join-Path $root "dist\SharpSaw-Setup-$Version.exe")"
