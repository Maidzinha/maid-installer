<#
  Instala (ou atualiza) o Maid Installer para o usuário atual. Não pede administrador e não mexe no registro.

  - Copia a pasta MaidInstaller\ (ao lado deste script) para %LocalAppData%\Programs\MaidInstaller.
  - Cria o atalho no Menu Iniciar (e, se você quiser, na Área de Trabalho).
  - Seus dados (configurações, listas, perfis, logs, modelo de IA) ficam em %LocalAppData%\MaidInstaller
    e NÃO são apagados numa atualização.

  -Sim responde "sim" às perguntas (uso em automação/testes); -Destino troca a pasta de instalação.
#>
param([switch]$Sim, [string]$Destino = (Join-Path $env:LOCALAPPDATA "Programs\MaidInstaller"), [switch]$SemAtalhoNaAreaDeTrabalho)
$ErrorActionPreference = "Stop"

$origem = Join-Path $PSScriptRoot "MaidInstaller"
$exe = Join-Path $origem "MaidInstaller.exe"
if (-not (Test-Path $exe)) { throw "Não encontrei $exe. Extraia o .zip inteiro antes de instalar." }
$versao = (Get-Item $exe).VersionInfo.ProductVersion
$menu = Join-Path ([Environment]::GetFolderPath("Programs")) "Maid Installer.lnk"
$desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "Maid Installer.lnk"
$dados = Join-Path $env:LOCALAPPDATA "MaidInstaller"

function Perguntar([string]$texto) {
    if ($Sim) { return $true }
    return (Read-Host "$texto (S/N)") -match '^[sS]'
}

$atualizar = Test-Path (Join-Path $Destino "MaidInstaller.exe")
if ((Test-Path $Destino) -and -not $atualizar -and (Get-ChildItem $Destino -Force | Select-Object -First 1)) {
    throw "A pasta $Destino já existe e não é uma instalação do Maid Installer. Nada foi alterado."
}
if (Test-Path (Join-Path $Destino "MaidInstaller.portable")) {
    throw "A pasta $Destino é uma cópia portátil. Nada foi alterado."
}

Write-Host ""
Write-Host "Maid Installer $versao" -ForegroundColor Magenta
Write-Host ($(if ($atualizar) { "Vai ATUALIZAR a instalação em:" } else { "Vai INSTALAR em:" })) $Destino
Write-Host "Atalho no Menu Iniciar: $menu"
Write-Host "Seus dados ficam em $dados (não são apagados)."
Write-Host "Não precisa de administrador e nada é alterado no registro do Windows."
Write-Host ""
if (-not (Perguntar "Continuar?")) { Write-Host "Cancelado. Nada foi alterado."; exit 1 }

if (Get-Process MaidInstaller -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Destino, [StringComparison]::OrdinalIgnoreCase) }) {
    throw "O Maid Installer está aberto. Feche-o e rode o instalador de novo. Nada foi alterado."
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
    $lnk.Description = "Maid Installer — Prepare seu PC com carinho"
    $lnk.Save()
}
Atalho $menu
if (-not $SemAtalhoNaAreaDeTrabalho -and (Perguntar "Criar atalho na Área de Trabalho também?")) { Atalho $desktop }

Write-Host ""
Write-Host "Pronto! O Maid Installer $versao está em $Destino ♡" -ForegroundColor Magenta
Write-Host "Para desinstalar: $(Join-Path $Destino 'Desinstalar.ps1')"
