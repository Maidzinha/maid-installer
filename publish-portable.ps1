<#
  Gera a versão portátil do Maid Installer (para pendrive / Ventoy).

  - "self-contained": leva o .NET 10 junto, então abre em qualquer Windows 10/11 x64,
    inclusive recém-formatado, sem instalar nada.
  - Cria o arquivo MaidInstaller.portable (ativa o modo portátil) e as pastas de dados.
  - Não copia logs, listas, perfis nem o modelo de IA deste PC: a pasta sai "limpa".
    (Para levar seus dados, use Configurações › Criar cópia portátil no próprio app.)

  Uso:  powershell -ExecutionPolicy Bypass -File .\publish-portable.ps1 [-Output <pasta>]
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot "dist\MaidInstaller")
)
$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "src\MaidInstaller.App\MaidInstaller.App.csproj"
if (Test-Path $Output) {
    if (-not (Test-Path (Join-Path $Output "MaidInstaller.portable")) -and (Get-ChildItem $Output -Force | Select-Object -First 1)) {
        throw "A pasta '$Output' já existe e não é uma versão portátil do Maid Installer. Escolha outra pasta."
    }
    # Só os arquivos do aplicativo são substituídos; as pastas de dados (se houver) ficam.
    Get-ChildItem $Output -Force | Where-Object { $_.Name -notin @("Data", "Config", "Profiles", "Lists", "Models", "Logs", "Cache") } |
        Remove-Item -Recurse -Force
}

Write-Host "Publicando (Release, win-x64, self-contained)..." -ForegroundColor Magenta
dotnet publish $project -c Release -r win-x64 --self-contained true -o $Output -nologo -v q -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou ($LASTEXITCODE)." }

# O pacote do llama.cpp traz também a versão ARM64; esta publicação é x64.
$arm = Join-Path $Output "runtimes\win-arm64"
if (Test-Path $arm) { Remove-Item $arm -Recurse -Force }

Set-Content -Path (Join-Path $Output "MaidInstaller.portable") -Encoding UTF8 `
    -Value "Este arquivo ativa o modo portátil do Maid Installer: os dados ficam nesta pasta."
foreach ($d in "Data", "Config", "Profiles", "Lists", "Models", "Logs", "Cache") {
    New-Item -ItemType Directory -Force (Join-Path $Output $d) | Out-Null
}

$size = (Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Pronto: {0} ({1:N0} MB)" -f $Output, ($size / 1MB)) -ForegroundColor Magenta
Write-Host "Copie a pasta inteira para o pendrive (no Ventoy, pode ser na raiz, ao lado das ISOs) e abra o MaidInstaller.exe."
