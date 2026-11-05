<#
  Instala (ou atualiza) o Maid Installer para o usuário atual. Não pede administrador e não mexe no registro.
  Installs (or updates) Maid Installer for the current user. No administrator rights, no registry changes.

  - Copia a pasta MaidInstaller\ (ao lado deste script) para %LocalAppData%\Programs\MaidInstaller.
  - Cria o atalho no Menu Iniciar (e, se você quiser, na Área de Trabalho).
  - Seus dados (configurações, listas, perfis, logs, modelo de IA) ficam em %LocalAppData%\MaidInstaller
    e NÃO são apagados numa atualização.

  As mensagens saem em português quando o Windows está em português; nos outros idiomas, em inglês.
  -Sim responde "sim" às perguntas (uso em automação/testes); -Destino troca a pasta de instalação.
#>
param([switch]$Sim, [string]$Destino = (Join-Path $env:LOCALAPPDATA "Programs\MaidInstaller"), [switch]$SemAtalhoNaAreaDeTrabalho)
$ErrorActionPreference = "Stop"

$pt = (Get-UICulture).TwoLetterISOLanguageName -eq "pt"
function T([string]$portugues, [string]$english) { if ($pt) { $portugues } else { $english } }

$origem = Join-Path $PSScriptRoot "MaidInstaller"
$exe = Join-Path $origem "MaidInstaller.exe"
if (-not (Test-Path $exe)) { throw (T "Não encontrei $exe. Extraia o .zip inteiro antes de instalar." "Could not find $exe. Extract the whole .zip before installing.") }
$versao = (Get-Item $exe).VersionInfo.ProductVersion
$menu = Join-Path ([Environment]::GetFolderPath("Programs")) "Maid Installer.lnk"
$desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "Maid Installer.lnk"
$dados = Join-Path $env:LOCALAPPDATA "MaidInstaller"

function Perguntar([string]$texto) {
    if ($Sim) { return $true }
    return (Read-Host "$texto $(T '(S/N)' '(Y/N)')") -match '^[sSyY]'
}

$atualizar = Test-Path (Join-Path $Destino "MaidInstaller.exe")
if ((Test-Path $Destino) -and -not $atualizar -and (Get-ChildItem $Destino -Force | Select-Object -First 1)) {
    throw (T "A pasta $Destino já existe e não é uma instalação do Maid Installer. Nada foi alterado." "The folder $Destino already exists and is not a Maid Installer installation. Nothing was changed.")
}
if (Test-Path (Join-Path $Destino "MaidInstaller.portable")) {
    throw (T "A pasta $Destino é uma cópia portátil. Nada foi alterado." "The folder $Destino is a portable copy. Nothing was changed.")
}

Write-Host ""
Write-Host "Maid Installer $versao" -ForegroundColor Magenta
Write-Host ($(if ($atualizar) { T "Vai ATUALIZAR a instalação em:" "This will UPDATE the installation at:" } else { T "Vai INSTALAR em:" "This will INSTALL to:" })) $Destino
Write-Host (T "Atalho no Menu Iniciar: $menu" "Start menu shortcut: $menu")
Write-Host (T "Seus dados ficam em $dados (não são apagados)." "Your data stays in $dados (it is not deleted).")
Write-Host (T "Não precisa de administrador e nada é alterado no registro do Windows." "No administrator rights needed and nothing is changed in the Windows registry.")
Write-Host ""
if (-not (Perguntar (T "Continuar?" "Continue?"))) { Write-Host (T "Cancelado. Nada foi alterado." "Canceled. Nothing was changed."); exit 1 }

if (Get-Process MaidInstaller -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Destino, [StringComparison]::OrdinalIgnoreCase) }) {
    throw (T "O Maid Installer está aberto. Feche-o e rode o instalador de novo. Nada foi alterado." "Maid Installer is open. Close it and run the installer again. Nothing was changed.")
}

if ($atualizar) {
    # Só arquivos do aplicativo existem aqui (os dados ficam em %LocalAppData%\MaidInstaller).
    Get-ChildItem $Destino -Force | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Force $Destino | Out-Null
Copy-Item (Join-Path $origem "*") -Destination $Destino -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
function Atalho([string]$caminho) {
    $lnk = $shell.CreateShortcut($caminho)
    $lnk.TargetPath = Join-Path $Destino "MaidInstaller.exe"
    $lnk.WorkingDirectory = $Destino
    $lnk.Description = T "Maid Installer — Prepare seu PC com carinho" "Maid Installer — Get your PC ready with care"
    $lnk.Save()
}
Atalho $menu
if (-not $SemAtalhoNaAreaDeTrabalho -and (Perguntar (T "Criar atalho na Área de Trabalho também?" "Also create a desktop shortcut?"))) { Atalho $desktop }

Write-Host ""
Write-Host (T "Pronto! O Maid Installer $versao está em $Destino ♡" "Done! Maid Installer $versao is in $Destino ♡") -ForegroundColor Magenta
Write-Host (T "Para desinstalar: " "To uninstall: ") (Join-Path $Destino 'Desinstalar.ps1')
