<#
.SYNOPSIS
  Construit SharpSaw.exe (exécutable portable, tout embarqué).
.DESCRIPTION
  1. Clone Chainsaw (version figée) et compile le crate chainsaw_ffi  -> native\chainsaw_ffi.dll
  2. Télécharge le modèle Gemma 3 1B Q4_K_M                            -> models\gemma-3-1b-it-Q4_K_M.gguf
  3. Publie l'application en un seul fichier                          -> dist\SharpSaw.exe
  Prérequis : .NET SDK 8, Rust (cargo), Git, Visual Studio Build Tools (C++).
#>
param(
    [string]$ChainsawCommit = 'af12749',
    [string]$ModelUrl = 'https://huggingface.co/unsloth/gemma-3-1b-it-GGUF/resolve/main/gemma-3-1b-it-Q4_K_M.gguf'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# 1. Moteur Chainsaw + bibliothèque FFI
$cs = Join-Path $root '.build\chainsaw'
if (-not (Test-Path $cs)) {
    git clone https://github.com/WithSecureOpenSource/chainsaw $cs
}
git -C $cs checkout --quiet $ChainsawCommit
git -C $cs checkout --quiet -- Cargo.toml Cargo.lock
git -C $cs apply --whitespace=nowarn (Join-Path $root 'native-src\chainsaw-workspace.patch')
Copy-Item (Join-Path $root 'native-src\chainsaw_ffi') (Join-Path $cs 'ffi') -Recurse -Force
Push-Location $cs
cargo build --release -p chainsaw_ffi
Pop-Location
Copy-Item (Join-Path $cs 'target\release\chainsaw_ffi.dll') (Join-Path $root 'native\chainsaw_ffi.dll') -Force

# 2. Modèle IA local
$model = Join-Path $root 'models\gemma-3-1b-it-Q4_K_M.gguf'
if (-not (Test-Path $model)) {
    New-Item -ItemType Directory -Force (Split-Path $model) | Out-Null
    Invoke-WebRequest $ModelUrl -OutFile $model
}

# 3. Publication
dotnet publish -c Release -r win-x64 -p:Platform=x64 -o (Join-Path $root 'dist')
Write-Host "OK : $(Join-Path $root 'dist\SharpSaw.exe')"
