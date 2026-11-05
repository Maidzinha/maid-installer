<#
  Desinstala o Maid Installer (instalação feita pelo Instalar.cmd).
  Uninstalls Maid Installer (installed with Instalar.cmd).

  - Remove a pasta do aplicativo (onde este script está) e os atalhos do Menu Iniciar e da Área de Trabalho.
  - Seus dados em %LocalAppData%\MaidInstaller (configurações, listas, perfis, logs, modelo de IA) só são
    apagados se você responder "S" à pergunta específica. Por padrão eles ficam.
  - Os programas que você instalou pelo Maid Installer NÃO são removidos.

  As mensagens saem em português quando o Windows está em português; nos outros idiomas, em inglês.
  -Sim confirma a desinstalação sem perguntar (os dados continuam mantidos; para apagá-los use também -ApagarDados).

  Uso / Usage: powershell -ExecutionPolicy Bypass -File "%LocalAppData%\Programs\MaidInstaller\Desinstalar.ps1"
#>
param([switch]$Sim, [switch]$ApagarDados)
$ErrorActionPreference = "Stop"

$pt = (Get-UICulture).TwoLetterISOLanguageName -eq "pt"
function T([string]$portugues, [string]$english) { if ($pt) { $portugues } else { $english } }
$sn = T "(S/N)" "(Y/N)"

$pasta = $PSScriptRoot
if (-not (Test-Path (Join-Path $pasta "MaidInstaller.exe")) -or (Test-Path (Join-Path $pasta "MaidInstaller.portable"))) {
    throw (T "Este script não está numa instalação do Maid Installer ($pasta). Nada foi alterado." "This script is not inside a Maid Installer installation ($pasta). Nothing was changed.")
}
$menu = Join-Path ([Environment]::GetFolderPath("Programs")) "Maid Installer.lnk"
$desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "Maid Installer.lnk"
$dados = Join-Path $env:LOCALAPPDATA "MaidInstaller"

Write-Host (T "Vai remover: $pasta e os atalhos do Maid Installer." "This will remove: $pasta and the Maid Installer shortcuts.")
Write-Host (T "Os programas instalados por ele continuam no PC." "The programs it installed stay on the PC.")
if (-not $Sim -and (Read-Host "$(T 'Desinstalar?' 'Uninstall?') $sn") -notmatch '^[sSyY]') { Write-Host (T "Cancelado." "Canceled."); exit 1 }
if (Get-Process MaidInstaller -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($pasta, [StringComparison]::OrdinalIgnoreCase) }) {
    throw (T "O Maid Installer está aberto. Feche-o e tente de novo. Nada foi alterado." "Maid Installer is open. Close it and try again. Nothing was changed.")
}

foreach ($lnk in $menu, $desktop) { if (Test-Path $lnk) { Remove-Item $lnk -Force } }

$apagar = $ApagarDados
if (-not $Sim -and (Test-Path $dados)) {
    $apagar = (Read-Host "$(T "Apagar também seus dados em $dados (listas, perfis, configurações, modelo de IA)?" "Also delete your data in $dados (lists, profiles, settings, AI model)?") $sn") -match '^[sSyY]'
}
if ($apagar -and (Test-Path $dados)) { Remove-Item $dados -Recurse -Force; Write-Host (T "Dados apagados." "Data deleted.") }
elseif (Test-Path $dados) { Write-Host (T "Seus dados foram mantidos em $dados." "Your data was kept in $dados.") }

# A pasta do app é apagada por um processo separado, depois que este script termina (ele está dentro dela).
Set-Location $env:TEMP
Start-Process -WindowStyle Hidden -FilePath "cmd.exe" -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$pasta`""
Write-Host (T "Maid Installer desinstalado. Até logo ♡" "Maid Installer uninstalled. See you ♡") -ForegroundColor Magenta
