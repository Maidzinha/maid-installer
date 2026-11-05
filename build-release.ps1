<#
  Gera a entrega completa do Maid Installer (versão Release).

  1. Roda os testes automatizados (para se algum falhar).
  2. Publica o app (Release, win-x64, self-contained: leva o .NET 10, abre em Windows recém-formatado).
  3. Monta dois pacotes .zip em dist\release:
       MaidInstaller-<versão>-portatil-win-x64.zip  → pasta MaidInstaller\ pronta para o pendrive / Ventoy
       MaidInstaller-<versão>-win-x64.zip           → versão para instalar no PC (Instalar.cmd, sem admin)
  4. Grava SHA256SUMS.txt e release.json (versão, data, arquivos e SHA-256) para conferir e para futuras atualizações.

  Nada daqui leva dados pessoais: sem configurações, listas, perfis, logs nem o modelo de IA.

  Uso:  powershell -ExecutionPolicy Bypass -File .\build-release.ps1 [-SkipTests]
#>
param([switch]$SkipTests)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = $PSScriptRoot
$csproj = Join-Path $root "src\MaidInstaller.App\MaidInstaller.App.csproj"
$version = ([xml](Get-Content $csproj -Raw -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Versão não encontrada em $csproj (<Version>)." }

$release = Join-Path $root "dist\release"
$staging = Join-Path $root "dist\staging"
foreach ($d in $release, $staging) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Force $release, $staging | Out-Null

if (-not $SkipTests) {
    Write-Host "Rodando os testes..." -ForegroundColor Magenta
    dotnet test (Join-Path $root "MaidInstaller.slnx") -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Há testes falhando; a entrega não foi gerada." }
}

# --- Portátil -------------------------------------------------------------------------------
$portableApp = Join-Path $staging "portatil\MaidInstaller"
& (Join-Path $root "publish-portable.ps1") -Output $portableApp
if (-not (Test-Path (Join-Path $portableApp "MaidInstaller.exe"))) { throw "A publicação não gerou o MaidInstaller.exe." }

# --- Instalável (mesmos binários, sem o marcador portátil e sem as pastas de dados) ---------
$installRoot = Join-Path $staging "instalavel"
$installApp = Join-Path $installRoot "MaidInstaller"
New-Item -ItemType Directory -Force $installApp | Out-Null
Get-ChildItem $portableApp -Force | Where-Object {
    $_.Name -ne "MaidInstaller.portable" -and $_.Name -notin @("Data", "Config", "Profiles", "Lists", "Models", "Logs", "Cache")
} | Copy-Item -Destination $installApp -Recurse -Force
$installer = Join-Path $root "installer"
Copy-Item (Join-Path $installer "Instalar.cmd"), (Join-Path $installer "Instalar.ps1") -Destination $installRoot
Copy-Item (Join-Path $installer "Desinstalar.ps1") -Destination $installApp

# --- Documentação junto dos pacotes ---------------------------------------------------------
foreach ($target in (Split-Path $portableApp), $installRoot) {
    $docs = Join-Path $target "LEIA-ME"
    New-Item -ItemType Directory -Force $docs | Out-Null
    Copy-Item (Join-Path $root "docs\INSTALACAO.md"), (Join-Path $root "docs\MANUAL.md"), (Join-Path $root "CHANGELOG.md") -Destination $docs
}

# --- Zips, SHA-256 e manifesto --------------------------------------------------------------
$packages = @(
    @{ Name = "MaidInstaller-$version-portatil-win-x64.zip"; Source = (Split-Path $portableApp); Kind = "portatil" },
    @{ Name = "MaidInstaller-$version-win-x64.zip"; Source = $installRoot; Kind = "instalavel" }
)
$files = @()
foreach ($p in $packages) {
    $zip = Join-Path $release $p.Name
    Write-Host "Compactando $($p.Name)..." -ForegroundColor Magenta
    [IO.Compression.ZipFile]::CreateFromDirectory($p.Source, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $files += [ordered]@{ name = $p.Name; kind = $p.Kind; bytes = (Get-Item $zip).Length; sha256 = $hash }
}
$sums = ($files | ForEach-Object { "$($_.sha256)  $($_.name)" }) -join "`n"
[IO.File]::WriteAllText((Join-Path $release "SHA256SUMS.txt"), $sums + "`n", (New-Object Text.UTF8Encoding $false))

$manifest = [ordered]@{
    schemaVersion = 1
    product = "Maid Installer — Personal Edition"
    version = $version
    builtAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    runtime = "win-x64, self-contained (.NET 10)"
    minimumWindows = "Windows 10 versão 1809 (build 17763) ou mais novo"
    files = $files
}
[IO.File]::WriteAllText((Join-Path $release "release.json"), ($manifest | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))
Remove-Item $staging -Recurse -Force

Write-Host ""
Write-Host "Entrega $version pronta em $release" -ForegroundColor Magenta
Get-ChildItem $release | ForEach-Object { Write-Host ("  {0,-45} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
