# Maid Installer — Personal Edition ♡

Aplicativo para Windows 10 e 11 que prepara PCs recém-formatados: instala, atualiza e organiza programas
com uma interface preto e rosa, a mascote Maid e a Maid Assistant. Uso pessoal de Helder.

*Prepare seu PC com carinho.*

## Para usar

Os pacotes prontos ficam em `dist\release` (gerados por `build-release.ps1`):

| Pacote | Uso |
|---|---|
| `MaidInstaller-1.0.0-portatil-win-x64.zip` | Pendrive / Ventoy: copie a pasta `MaidInstaller\` e abra o `.exe` |
| `MaidInstaller-1.0.0-win-x64.zip` | Instalar no PC: extraia e rode `Instalar.cmd` (sem administrador) |

Os dois levam o .NET junto e abrem em Windows recém-formatado.

- [Instalação](docs/INSTALACAO.md): requisitos, portátil, instalar, atualizar, desinstalar e configuração padrão
- [Manual de uso](docs/MANUAL.md): todas as telas e a Maid Assistant
- [Histórico de versões](CHANGELOG.md)

## Para compilar

Requisitos: Windows 10 1809+ ou Windows 11 e o .NET SDK 10 (`dotnet --list-sdks`).

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/MaidInstaller.App
```

Entrega completa (testes, publicação Release self-contained, dois `.zip`, `SHA256SUMS.txt` e `release.json`):

```bash
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

Só a pasta portátil (sai em `dist\MaidInstaller`):

```bash
powershell -ExecutionPolicy Bypass -File .\publish-portable.ps1
```

## Estrutura

| Caminho | O quê |
|---|---|
| `src/MaidInstaller.Core` | Lógica (WinGet, fila, catálogo, perfis, Kit, assistente, armazenamento) |
| `src/MaidInstaller.AI` | IA local (llama.cpp via LLamaSharp) |
| `src/MaidInstaller.App` | Interface WPF; `Catalog/` (dados), `Defaults/settings.default.json` (configuração padrão), `Assets/` |
| `tests/MaidInstaller.Core.Tests` | Testes xUnit |
| `installer/` | `Instalar.cmd`, `Instalar.ps1`, `Desinstalar.ps1` (vão no pacote instalável) |

## Documentação técnica

- [docs/ARQUITETURA.md](docs/ARQUITETURA.md): arquitetura, pastas e regras de segurança
- [docs/ROADMAP.md](docs/ROADMAP.md): etapas e o que funciona de verdade
- [docs/TESTES.md](docs/TESTES.md): testes reais e limitações
- [docs/ATUALIZACOES.md](docs/ATUALIZACOES.md): como lançar versões novas sem perder dados
- [docs/ICONES.md](docs/ICONES.md): origem dos ícones dos apps
