<#
  Desinstala o Maid Installer (instalação feita pelo Instalar.cmd).

  - Remove a pasta do aplicativo (onde este script está) e os atalhos do Menu Iniciar e da Área de Trabalho.
  - Seus dados em %LocalAppData%\MaidInstaller (configurações, listas, perfis, logs, modelo de IA) só são
    apagados se você responder "S" à pergunta específica. Por padrão eles ficam.
  - Os programas que você instalou pelo Maid Installer NÃO são removidos.

  -Sim confirma a desinstalação sem perguntar (os dados continuam mantidos; para apagá-los use também -ApagarDados).

  Uso: powershell -ExecutionPolicy Bypass -File "%LocalAppData%\Programs\MaidInstaller\Desinstalar.ps1"
#>
param([switch]$Sim, [switch]$ApagarDados)
$ErrorActionPreference = "Stop"

$pasta = $PSScriptRoot
if (-not (Test-Path (Join-Path $pasta "MaidInstaller.exe")) -or (Test-Path (Join-Path $pasta "MaidInstaller.portable"))) {
    throw "Este script não está numa instalação do Maid Installer ($pasta). Nada foi alterado."
}
$menu = Join-Path ([Environment]::GetFolderPath("Programs")) "Maid Installer.lnk"
$desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "Maid Installer.lnk"
$dados = Join-Path $env:LOCALAPPDATA "MaidInstaller"

Write-Host "Vai remover: $pasta e os atalhos do Maid Installer."
Write-Host "Os programas instalados por ele continuam no PC."
if (-not $Sim -and (Read-Host "Desinstalar? (S/N)") -notmatch '^[sS]') { Write-Host "Cancelado."; exit 1 }
if (Get-Process MaidInstaller -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($pasta, [StringComparison]::OrdinalIgnoreCase) }) {
    throw "O Maid Installer está aberto. Feche-o e tente de novo. Nada foi alterado."
}

foreach ($lnk in $menu, $desktop) { if (Test-Path $lnk) { Remove-Item $lnk -Force } }

$apagar = $ApagarDados
if (-not $Sim -and (Test-Path $dados)) {
    $apagar = (Read-Host "Apagar também seus dados em $dados (listas, perfis, configurações, modelo de IA)? (S/N)") -match '^[sS]'
}
if ($apagar -and (Test-Path $dados)) { Remove-Item $dados -Recurse -Force; Write-Host "Dados apagados." }
elseif (Test-Path $dados) { Write-Host "Seus dados foram mantidos em $dados." }

# A pasta do app é apagada por um processo separado, depois que este script termina (ele está dentro dela).
Set-Location $env:TEMP
Start-Process -WindowStyle Hidden -FilePath "cmd.exe" -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$pasta`""
Write-Host "Maid Installer desinstalado. Até logo ♡" -ForegroundColor Magenta
