# Testes — Maid Installer (Etapa 10)

Tudo o que está aqui foi executado de verdade, no PC de desenvolvimento (Windows 11 Pro 26200, 1920×1080),
em 04/11/2026. O que **não** foi testado está listado no fim, sem esconder nada.

## Testes automatizados

`dotnet test MaidInstaller.slnx` → **175 aprovados, 0 falhas** (xUnit, projeto `MaidInstaller.Core.Tests`).

Cobrem: catálogo e IDs, perfis/listas (JSON, importar/exportar), fila de instalação e códigos de saída do WinGet,
leitura do `winget upgrade`, verificações do Kit Pós-Formatação, modo portátil (caminhos relativos, marcador, cópia),
base de conhecimento e a Maid Assistant (respostas prontas, montagem do prompt, `AnswerGuard.Clean/Ground/Check`).

## Testes reais pela interface

| Área | O que foi feito | Resultado |
|---|---|---|
| Instalação | Telegram Desktop pela Biblioteca, com a janela de confirmação | ✅ Concluído; `winget list` mostra 7.3.3; ícone aparece na tela de Instalações |
| Atualização | Ollama 0.40.13 → 0.40.14 pela tela Atualizações, com confirmação | ✅ Concluído e conferido no `winget list`. Achado: a mensagem dizia "Instalação concluída" → corrigido para "Atualização concluída" (teste `Successful_upgrade_says_update_not_install`) |
| Sem WinGet | WinGet escondido do app (simulação) | ✅ Avisos claros ("WinGet não encontrado neste PC…"), status pelo Menu Iniciar, nada quebra |
| Download da IA | Configurações › Baixar IA local (491.400.032 bytes) | ✅ ~13 s; SHA-256 conferido pelo app **e** de forma independente (`74a4da8c…d7a9db`) |
| IA — 1ª rodada | Perguntas reais ao modelo | ❌ Comparação VLC×Spotify misturou fatos; PowerToys ganhou frase inventada; "Qual navegador?" escolheu sem motivo |
| IA — correções | Comparação e categoria agora vêm prontas da base; novo `AnswerGuard.Ground` remove frases sem apoio nos fatos (e descarta a resposta se a 1ª frase for inventada) | ✅ 7-Zip, OBS, Steam, PowerToys corretos; Telegram/Python caem na base. Cada frase removida fica no log |
| Ícones | Biblioteca, Instalações, Atualizações | ✅ Todos os 39 apps com ícone (oficial embutido; instalados usam o ícone real do Windows) — ver [ICONES.md](ICONES.md) |
| Pendrive Ventoy real (D:, exFAT) | "Criar cópia portátil" com o modelo → `D:\MaidInstaller` (403 arquivos, 733 MB) e app aberto a partir do D: | ✅ Modo portátil, dados/logs/config em `D:\MaidInstaller`, IA carregada de `D:\MaidInstaller\Models` e respondendo, **nenhum arquivo** gravado em `%LocalAppData%\MaidInstaller` |
| Troca de letra | Mesma pasta portátil como `X:` e depois `W:` (`subst`) | ✅ Dados mantidos (Etapa 9) |

## Etapa 11 — pacotes de entrega

Os testes automatizados passaram a ser **181**: configuração padrão, migração e preservação de campos de versões mais novas.

| Teste | Resultado |
|---|---|
| `build-release.ps1` (testes + publicação + zips) | ✅ Dois `.zip` de ~104 MB, `SHA256SUMS.txt` e `release.json` |
| Zip portátil extraído com `Expand-Archive` e aberto | ✅ Modo portátil, configuração padrão lida de `Defaults\`, perfis criados, nada em `%LocalAppData%` |
| `Instalar.ps1` (instalação nova) | ✅ App em `%LocalAppData%\Programs\MaidInstaller`, atalho no Menu Iniciar apontando para o `.exe` |
| Abrir pelo atalho | ✅ Modo instalado, dados existentes usados, IA carregada |
| Atualizar com o app aberto | ✅ Recusado, nada alterado |
| Atualizar com o app fechado | ✅ Arquivos trocados, perfis mantidos |
| `Desinstalar.ps1` | ✅ Programa e atalho removidos; dados mantidos (11/11 arquivos, modelo incluído) |
| Destino com arquivos de outra coisa | ✅ Recusado, arquivo preservado |

Não testado: o duplo clique no `Instalar.cmd` com as perguntas interativas. O teste usou `-Sim`, que pula só as perguntas e roda o mesmo código.

## Limitações conhecidas

- **Modelo pequeno (0,5B)**: às vezes erra o português ("compreensão", "extende") e às vezes a resposta inteira é descartada pelo
  `Ground`, caindo na resposta da base. Ele nunca instala nada e não passa informação que não esteja na base.
- **Windows 10 real não testado**: só Windows 11. O alvo `net10.0-windows10.0.19041.0` e as APIs usadas existem no Windows 10 2004+,
  mas não houve máquina para confirmar.
- **Não testado de propósito** (destrutivo ou fora da autorização): limpeza de temporários, remoção de componentes, alterações de
  configuração do Windows pelo Kit (só as verificações, que são somente leitura) e qualquer reinício.
- PC recém-formatado sem nada instalado: não testado. A versão portátil é self-contained, então não depende do .NET instalado.

## Efeitos colaterais no PC de teste

- Telegram Desktop **instalado** e Ollama **atualizado** (testes reais).
- O modelo de IA (469 MB) está em `%LocalAppData%\MaidInstaller\Models`, em `dist\MaidInstaller\Models` e em `D:\MaidInstaller\Models`.
