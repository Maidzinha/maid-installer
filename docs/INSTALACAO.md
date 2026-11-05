# Instalação — Maid Installer ♡

**Português** · [English](en/INSTALL.md)

O Maid Installer vem em dois pacotes. Os dois levam o .NET junto, então abrem até num Windows recém-formatado,
sem instalar nada antes.

| Pacote | Para quê | Onde ficam seus dados |
|---|---|---|
| `MaidInstaller-<versão>-portatil-win-x64.zip` | Pendrive / Ventoy: roda direto, sem instalar | Na própria pasta `MaidInstaller\` do pendrive |
| `MaidInstaller-<versão>-win-x64.zip` | Deixar instalado no PC | `%LocalAppData%\MaidInstaller` |

## Requisitos

- Windows 10 versão 1809 ou mais novo, ou Windows 11 (64 bits, x64).
- Cerca de 270 MB livres para o app. A IA local é opcional e precisa de mais 470 MB.
- Para instalar programas, o **WinGet** (Instalador de Aplicativo da Microsoft). Ele já vem no Windows 11 e nos Windows 10 atualizados.
  Sem ele o app abre normalmente, avisa e mostra como obtê-lo (Kit Pós-Formatação › WinGet).
- Internet só para baixar programas, consultar atualizações e baixar a IA. A base da Maid Assistant funciona offline.

## Conferir o download (opcional)

Junto dos `.zip` vem o `SHA256SUMS.txt`. No PowerShell:

```powershell
Get-FileHash .\MaidInstaller-1.1.0-win-x64.zip -Algorithm SHA256
```

O resultado precisa ser igual ao do arquivo (sem diferenciar maiúsculas e minúsculas).

## Versão portátil (pendrive / Ventoy)

1. Extraia o `.zip`. Ele contém a pasta `MaidInstaller\`.
2. Copie a pasta `MaidInstaller\` inteira para o pendrive. No Ventoy, pode ficar na raiz da partição exFAT, ao lado das ISOs.
3. Depois de formatar o PC, abra `MaidInstaller\MaidInstaller.exe` direto do pendrive.

O arquivo `MaidInstaller.portable` ao lado do `.exe` liga o modo portátil. Configurações, listas, perfis, logs e o modelo de IA
ficam no pendrive, e a letra da unidade pode mudar de um PC para outro. No PC ficam só os programas que você mandar instalar
e os arquivos temporários do WinGet.

Também dá para criar a cópia pelo próprio app: **Configurações › Criar cópia portátil…**. Assim vão junto suas configurações,
perfis e listas (e o modelo de IA, se marcar a opção).

> O Windows pode mostrar o aviso do SmartScreen ("O Windows protegeu o computador"), porque o app não tem assinatura digital
> comercial. Se o arquivo veio da sua própria compilação, clique em **Mais informações › Executar assim mesmo**.

## Versão instalada

1. Extraia o `.zip` inteiro numa pasta qualquer (por exemplo, Downloads).
2. Dê dois cliques em **`Instalar.cmd`**.
3. O instalador mostra o que vai fazer e pede confirmação (no idioma do Windows):
   - copia o app para `%LocalAppData%\Programs\MaidInstaller`;
   - cria o atalho **Maid Installer** no Menu Iniciar e, se você quiser, na Área de Trabalho.

O instalador **não pede administrador**, não altera o registro do Windows e não instala nada além do próprio Maid Installer.

### Atualizar

Rode o `Instalar.cmd` da versão nova. Ele troca só os arquivos do programa. Seus dados em `%LocalAppData%\MaidInstaller`
continuam lá. Feche o Maid Installer antes.

### Desinstalar

No PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\MaidInstaller\Desinstalar.ps1"
```

O desinstalador remove o programa e os atalhos. Seus dados só são apagados se você responder **S** (ou **Y**, em inglês) à pergunta específica.
Os programas instalados pelo Maid Installer **continuam no PC**.

## Primeira execução, idioma e configuração padrão

Na primeira vez, o app lê `Defaults\settings.default.json` (na pasta do programa) e cria seu `Config\settings.json`.
Uma instalação nova abre em **inglês**, no tema **Escuro** e sem nome de usuário.

Para trocar idioma, tema, nome ou favoritos, use **Configurações** no app (idioma e tema valem ao reabrir o app; só o app
reabre, nunca o PC). Para deixar tudo pronto **antes** da primeira abertura (por exemplo, num pendrive novo), edite o
`settings.default.json` no Bloco de Notas:

| Campo | Valores |
|---|---|
| `language` | `"en"` (inglês), `"pt-BR"` (português), `"auto"` (o mesmo do Windows) |
| `theme` | `"Dark"` (escuro), `"Light"` (claro), `"BlackPink"` (preto e rosa) |
| `userName` | Como a Maid te chama; `""` = sem nome |

Depois disso, o que vale é o `settings.json` e as mudanças feitas pelo app.

## Compilar a partir do código

Requer o .NET SDK 10. Na pasta do projeto:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

O script roda os testes, publica em Release e gera os dois `.zip`, o `SHA256SUMS.txt` e o `release.json` em `dist\release`.
