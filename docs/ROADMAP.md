# Roadmap — Maid Installer

Legenda: ✅ concluído e testado · 🟡 parcial · ⏳ pendente

| Etapa | Status | O que existe de verdade |
|---|---|---|
| 1 — Planejamento | ✅ | Arquitetura, solução .NET 10, pastas, padrões ([ARQUITETURA.md](ARQUITETURA.md)) |
| 2 — Interface | ✅ | Janela própria preto e rosa, barra lateral, tela inicial, mascote, 6 atalhos, acesso rápido, Status do Sistema, painel da Maid Assistant |
| 3 — Biblioteca | ✅ | Catálogo JSON com 39 apps (36 + 3 runtimes da Etapa 6) em 8 categorias, IDs WinGet verificados, pesquisa, filtros, seleção múltipla e status real de instalação |
| 4 — Instalação (WinGet) | ✅ | Fila real com WinGet: confirmação antes de instalar, um app por vez, etapa real (baixando/instalando), cancelar, tentar de novo, log por app e erros explicados |
| 5 — Perfis e listas | ✅ | Perfis prontos (PC Gamer, Trabalho, Completo) e personalizados, Minhas Listas, edição com salvamento automático em JSON, duplicar, excluir, importar/exportar e instalação em lote |
| 6 — Kit Pós-Formatação | ✅ | 10 cartões independentes com verificações reais (só leitura) e ações opcionais, todas com confirmação |
| 7 — Atualizações | ✅ | Lista real do `winget upgrade` (versão instalada e disponível), atualização individual e dos selecionados com confirmação, histórico salvo |
| 8 — Maid Assistant (IA) | ✅ | IA local opcional (Qwen2.5 0.5B GGUF Q4 via llama.cpp, contexto 1024) limitada aos fatos da base; respostas sem IA para tudo; códigos de erro, links e categorias; download com progresso real e SHA-256; página de Configurações |
| 9 — Modo portátil | ✅ | Versão portátil autocontida (`publish-portable.ps1`, leva o .NET), "Criar cópia portátil" pelo app, dados no pendrive, nada gravado no PC; testado com troca de letra de unidade |
| 10 — Testes | ✅ | Testes automatizados (175 ao fim da etapa); instalação e atualização reais, IA real com correções (`AnswerGuard.Ground`), ícones de todos os apps, pendrive Ventoy real — ver [TESTES.md](TESTES.md) |
| 11 — Compilação e entrega | ✅ | `build-release.ps1`: Release self-contained, pacote portátil e instalável (`Instalar.cmd`, sem admin), SHA-256 e `release.json`; configuração padrão; [instalação](INSTALACAO.md), [manual](MANUAL.md), [atualizações](ATUALIZACOES.md); 181 testes |

## Detalhes da Etapa 2

Funciona de verdade:
- **Status do Sistema**: versão do Windows, CPU, GPU (com VRAM), RAM instalada, uso real de CPU/GPU/RAM a cada 1,5 s,
  temperatura da GPU NVIDIA (via `nvidia-smi`) e zona térmica ACPI quando o PC permite. Quando não há suporte, aparece "n/d" ou "Não suportada".
- **Acesso Rápido**: localiza os favoritos no Menu Iniciar (inclusive apps da Microsoft Store), mostra o ícone real e abre o programa.
  Os não instalados aparecem apagados, e a assistente avisa ao clicar neles.
- **Maid Assistant**: conversa, sugestões, minimizar (fica salvo), respostas só da base local.
- **Sobre**: versão, modo, pastas reais e botões para abrir a pasta de dados e os logs.
- **Navegação**: os atalhos e a barra lateral abrem as páginas. As que ainda não existem dizem em qual etapa serão entregues.

Limitações conhecidas:
- Temperatura da CPU: o Windows não oferece uma API padrão sem privilégios. A zona ACPI costuma exigir administrador.

## Detalhes da Etapa 3

- **Catálogo** (`Catalog/catalog.json`): cada app tem nome, ícone, descrição, categoria, desenvolvedor, preço, licença,
  tamanho (quando conhecido), link oficial, ID WinGet, versão verificada e nomes do Menu Iniciar.
- **IDs WinGet**: todos conferidos com `winget show --id <id> -e` em 2026-11-04. Três apps não têm pacote no WinGet e ficam só com o link oficial:
  Riot Client (vem com cada jogo da Riot), NVIDIA App e AMD Software: Adrenalin Edition. O pacote "AMD Software: Cloud Edition" é outro produto e não foi usado.
- **Validação** (`CatalogValidator`): ao abrir, descarta entradas com ID de formato estranho (espaços, `&`, `;`, `--`), fonte diferente de winget/msstore,
  link que não seja HTTPS ou categoria inexistente. O catálogo nunca contém comandos.
- **Status de instalação**: vem do `winget export` (JSON). O que o WinGet não reconhece, como o WhatsApp da Loja, é detectado pelo Menu Iniciar.
  O resultado fica em `Cache/installed-cache.json`, então a Biblioteca abre na hora e atualiza em segundo plano (cerca de 20 s).
- **Versão disponível**: a do catálogo aparece sempre. O botão "Consultar versão atual" pergunta ao WinGet na hora.
- **Pesquisa e filtros**: nome, função, desenvolvedor e apelidos, sem diferenciar acentos (ex.: "gravar tela" acha OBS e ShareX). Filtros por categoria,
  Instalados/Não instalados, Só gratuitos e Só com WinGet.
- **Seleção**: caixa em cada cartão, "Selecionar visíveis" e "Limpar seleção".
- **Acesso Rápido**: "Adicionar/Remover do Acesso Rápido" em cada app. O botão "+" da tela inicial abre a Biblioteca.
- **Maid Assistant**: agora conhece todos os apps do catálogo (usando só os dados verificados dele).

Os botões "Instalar" e "Instalar selecionados" passaram a funcionar na Etapa 4.

Detalhe técnico: o WinGet é um app empacotado (MSIX) e não enxerga pastas criadas por outros apps em `%LocalAppData%`.
Por isso o `winget export` grava em `%TEMP%`, e o arquivo é apagado logo depois.

## Detalhes da Etapa 4

- **Confirmação**: antes de qualquer instalação aparece uma janela com a lista exata do que será instalado (nome e ID WinGet/Store)
  e os avisos: fontes oficiais, modo silencioso, UAC, aceite de licenças, nada será atualizado e o PC não reinicia sozinho.
  Apps já instalados, já na fila ou sem pacote WinGet são separados e listados à parte.
- **Comando**: `winget install --id <id> --exact --source <winget|msstore> --silent --no-upgrade --accept-package-agreements
  --accept-source-agreements --disable-interactivity`. Sem `--no-upgrade`, o WinGet atualizaria um programa já instalado — e atualizar exige autorização.
- **Fila**: um app por vez (`InstallationService`, worker sequencial). Estados: Aguardando, Baixando, Instalando, Concluído, Já instalado, Falhou, Cancelado.
- **Progresso honesto**: com a saída redirecionada o WinGet não envia barra de progresso, só as etapas. A tela mostra a etapa real e um indicador
  de atividade animado; a porcentagem só aparece se o WinGet informar números reais (`X MB / Y MB` ou `N%`).
- **Erros**: os códigos do WinGet foram conferidos com `winget error` e têm mensagens em português (sem internet, disco cheio, arquivo em uso,
  UAC recusado, bloqueado por política…). Códigos desconhecidos são explicados pelo próprio `winget error --input`.
- **Reinício**: quando o instalador pede reinício, aparece um aviso. O app nunca reinicia o PC.
- **Logs**: cada instalação gera `Logs/Instalacoes/<data>-<app>.log` com o comando e toda a saída; o botão "Ver log" abre no Bloco de Notas.
- **Cancelar**: tira da fila o que não começou e encerra o processo do WinGet. Um instalador elevado (UAC) que já começou pode terminar sozinho — o app avisa isso.
- **Fechar o app** com instalações em andamento pede confirmação.
- **Maid Assistant**: avisa quando começa ("Helder, estou instalando o Discord! ♡"), quando termina e quando falha, e responde
  "como está a instalação?" com o estado real da fila. Ela só comenta; quem instala é o motor.
- **Depois da fila**: a Biblioteca e o Acesso Rápido atualizam o status sozinhos.
## Detalhes da Etapa 5

- **Formato**: perfis e listas são "coleções" com nome, descrição, ícone e os ids dos apps do catálogo. Cada uma é um arquivo
  `Profiles/<id>.json` ou `Lists/<id>.json` na pasta de dados (ou no pendrive). Como ficam fora da pasta do programa, sobrevivem a atualizações.
- **Perfis prontos** (`Catalog/profiles.json`): PC Gamer, PC Trabalho e PC Completo, só com apps que têm pacote WinGet verificado.
  Na primeira abertura são copiados para `Profiles/` e passam a ser editáveis. Um perfil pronto excluído não volta sozinho;
  o botão "Restaurar perfis prontos" recria os que faltam. Drivers NVIDIA/AMD ficam de fora do PC Gamer (não têm pacote WinGet) e isso está escrito na descrição.
- **Personalizado**: "Novo perfil"/"Nova lista" cria um item e abre o seletor de apps do catálogo (com pesquisa).
- **Edição**: renomear e descrição (salvam ao sair do campo ou com Enter), adicionar e remover apps. Tudo é salvo na hora, com aviso "salvo às HH:mm".
  Nome vazio não é aceito; nomes repetidos ganham um número.
- **Duplicar** cria uma cópia "(cópia)". **Excluir** pede confirmação e só apaga o arquivo da lista — nenhum programa é desinstalado.
- **Exportar/Importar**: arquivo `.maidlist.json` com id do catálogo, ID WinGet e nome de cada app. Na importação, só entram apps que existem
  no catálogo verificado (casando pelo id ou pelo ID WinGet); o resto é listado e descartado. Arquivos de outro formato, de versão mais nova,
  JSON inválido ou maiores que 512 KB são recusados com mensagem clara.
- **Instalar todos**: usa a mesma confirmação e a mesma fila da Etapa 4 (pula já instalados e apps sem pacote).
- **Biblioteca**: novo botão "Salvar como lista" cria uma lista com os apps selecionados.
- **Segurança**: ids viram nomes de arquivo só se forem `a-z0-9-` (sem `..`, barras ou espaços). Arquivos corrompidos são ignorados e registrados no log, nunca apagados.

Testado na interface: perfis prontos criados, nova lista com seletor, renomear, duplicar, excluir com confirmação e "Instalar todos" (cancelado na confirmação).
As janelas de Exportar/Importar (diálogos do Windows) não foram clicadas pela automação; a leitura e a gravação dos arquivos estão cobertas pelos testes.
## Detalhes da Etapa 6

Cada cartão é independente, explica o que faz e separa **verificação** (só lê) de **ação** (pede confirmação mostrando o que muda).

| Cartão | Verificação real | Ação opcional |
|---|---|---|
| Atualizações do Windows | Última atualização instalada (WMI `Win32_QuickFixEngineering`) | "Procurar atualizações" pela API oficial do Windows Update (só procura); abrir o Windows Update. O app não instala atualizações nem reinicia o PC |
| WinGet | `winget --version` | Abrir o Instalador de Aplicativo na Microsoft Store |
| Componentes necessários | Visual C++ x64/x86 e DirectX (status da Biblioteca) | "Instalar os que faltam" → confirmação + fila da Etapa 4 |
| Runtimes | .NET Desktop 8 e 10 (pasta oficial do .NET) e Java (registro/PATH), além do WinGet | "Instalar os que faltam" → confirmação + fila |
| Drivers | Dispositivos com erro (WMI `Win32_PnPEntity`) e versão/data do driver de vídeo | Abrir Gerenciador de Dispositivos e drivers opcionais do Windows Update |
| Arquivos temporários | Tamanho total e o que tem mais de 24 h em `%TEMP%` | "Limpar": só arquivos antigos do `%TEMP%` do usuário; pula em uso/somente leitura; não segue junções; não toca em `C:\Windows\Temp` |
| Opções de energia | Planos existentes e o ativo (`powercfg /list`) | Ativar um plano que já existe (`powercfg /setactive`); nenhum plano é criado ou apagado |
| Programas de inicialização | Registro (Run, Run 32 bits) e pastas Inicializar, com ativo/desativado (StartupApproved) | Só consulta; abre o Gerenciador de Tarefas ou as Configurações |
| Extensões de arquivos | `HideFileExt` do usuário | Mostrar/esconder extensões (HKCU, sem administrador); abrir Aplicativos padrão |
| Ferramentas administrativas | — | Abrir 8 ferramentas fixas do Windows (lista no código, sem argumentos) |

Novos no catálogo (IDs conferidos com `winget show` em 2026-11-04): `Microsoft.DotNet.DesktopRuntime.8`, `Microsoft.DotNet.DesktopRuntime.10`, `EclipseAdoptium.Temurin.21.JRE`.

Testado no PC: todas as verificações com dados reais; a busca do Windows Update achou 1 atualização pendente (Defender) em cerca de 30 s;
as confirmações de "Limpar" e "Ativar plano" foram abertas e **canceladas** (nada mudou). A aplicação real de limpeza, troca de plano e
extensões ainda não foi feita no PC do Helder — só nos testes automáticos (pasta e chave de registro de teste).
## Detalhes da Etapa 7

- **Consulta**: `winget upgrade` (só leitura). A tabela é lida pela posição das colunas, então funciona com o WinGet em qualquer idioma.
  IDs cortados com "…" são completados pela lista do `winget export`; o que não der para confirmar fica de fora (e vai para o log).
  Só entram pacotes com fonte `winget`/`msstore` e ID em formato válido.
- **Lista**: nome, ID, versão instalada → disponível, ícone real (do catálogo ou do Menu Iniciar). Ao abrir a página a consulta roda uma vez;
  o botão "Verificar atualizações" repete.
- **Atualizar**: botão em cada programa ou "Atualizar selecionados". Sempre abre a confirmação com a lista e as versões.
  O comando é `winget upgrade --id <id> --exact --source <fonte> --silent ...` — nunca `--all`. Usa a mesma fila, logs, cancelamento e
  tratamento de erros da Etapa 4. O App Installer (o próprio WinGet) fica sempre por último.
- **Histórico** (`Data/historico.json`): toda instalação e atualização que chegou a rodar, com data, versões, resultado e mensagem (até 500 itens).
- **Depois da fila**: a lista de atualizações é consultada de novo sozinha.

Testado no PC: 17 atualizações reais listadas (Git 2.56.0.3 → 2.56.0.4, Node.js 24.22.0 → 26.13.0, Teams, .NET SDK…);
"Selecionar todos" + "Atualizar selecionados" abriu a confirmação e foi **cancelado** — nenhum programa foi atualizado.

## Detalhes da Etapa 8

- **Base de conhecimento** (`Catalog/knowledge.json` + catálogo): 39 apps (10 com ficha detalhada, os outros com os dados verificados
  do catálogo) e 13 temas (essenciais, Kit, WinGet, modo portátil, administrador/UAC, reinício, segurança do download, código aberto,
  atualizações, DLL faltando, desinstalar, IA local, quem é a Maid).
- **Busca** (`KnowledgeRetriever`): encontra os apps citados (nome e apelidos), o tema e a categoria ("qual navegador…").
- **Respostas sem IA** (`OfflineResponder`), sempre disponíveis: descrição, preço, desenvolvedor, site oficial, comparação, categoria,
  temas e **códigos de erro do WinGet** (`0x8A150105` ou `-1978335135`, pela mesma tabela da Etapa 4). Assunto fora da base →
  "ainda não tenho essa informação cadastrada".
- **IA local** (`MaidInstaller.AI/LlamaChatModel`): LLamaSharp 0.30 (llama.cpp), só CPU, contexto 1024 tokens, até 200 tokens de
  resposta, temperatura 0,3, formato ChatML. É opcional e baixada à parte.
- **Híbrido** (`HybridResponder`): dados exatos (erro, preço, desenvolvedor, link, saudação, temas revisados) e perguntas sem
  correspondência **nunca** passam pela IA. Explicações, comparações e categorias usam a IA com um prompt que contém **só** os fatos
  encontrados (máx. 1600 caracteres). A resposta aparece enquanto é escrita.
- **Conferência** (`AnswerGuard`): se a IA citar número ou link que não está nos fatos, escrever em outro alfabeto, falhar ou demorar
  mais de 90 s, a resposta é trocada pela da base (e isso vai para o log). Passar o mouse sobre a mensagem mostra a origem.
- **Fila de instalação**: perguntas como "como está a instalação?" usam o estado real da fila; a Maid comenta início/fim de cada app.
- **Download do modelo** (Configurações › Maid Assistant): Qwen2.5-0.5B-Instruct Q4_K_M do repositório oficial `Qwen/` no Hugging Face
  (468,6 MB, Apache-2.0). Endereço fixo no código, confirmação antes, checagem de espaço livre, progresso real em bytes, cancelar,
  arquivo `.part` até conferir **tamanho e SHA-256** (`74a4da8c…d7a9db`). Também aceita outro `.gguf` colocado na pasta `Models/`.
- **Configurações**: nome do usuário, estado da IA, baixar, ligar/desligar, remover modelo (com confirmação), abrir a pasta, procurar de novo.

Testado no PC: página de Configurações, confirmação do download aberta e **cancelada** (nada baixado), chat sem IA respondendo
código de erro, categoria e site oficial. A geração real com o modelo depende do download (ver relatório da etapa).

## Detalhes da Etapa 9

- **Detecção**: o arquivo `MaidInstaller.portable` ao lado do `.exe` ativa o modo portátil; todos os dados ficam na pasta do app
  (Data, Config, Profiles, Lists, Models, Logs, Cache). Nenhum caminho absoluto é salvo, então a letra do pendrive pode mudar de um PC para outro.
- **Versão portátil** (`publish-portable.ps1`): `dotnet publish` Release, win-x64, **self-contained** (leva o .NET 10 e abre num Windows
  recém-formatado), só traduções pt-BR, sem a parte ARM64 do llama.cpp. Cria o marcador e as pastas. Sai em `dist\MaidInstaller` (~264 MB).
- **Criar cópia portátil** (Configurações › Modo portátil e armazenamento): escolhe o pendrive/pasta → cria `MaidInstaller\` lá dentro,
  com os arquivos do app + configurações, perfis e listas. **Não** leva logs, cache nem o histórico deste PC; o modelo de IA só vai se
  marcar "Levar o modelo de IA junto". Confirmação com destino, nº de arquivos, tamanho e espaço livre; checa espaço antes; progresso
  real em bytes; cancelar. Se o destino já for uma cópia portátil, atualiza os arquivos do app e **mantém** as listas/perfis de lá.
  Recusa destinos dentro do próprio app/dados e pastas que já têm outra coisa. Avisa quando a versão em uso depende do .NET instalado.
- **Pegadas no PC**: janelas de arquivo não entram na lista de "recentes" do Windows e, no modo portátil, abrem no pendrive.
  Ficam no PC só os temporários do `winget export` (apagados em seguida), os logs internos do próprio WinGet e o que o usuário mandar
  instalar/alterar.
- **Gravação segura em pendrive**: JSON e arquivos copiados são gravados em `.tmp`, enviados ao disco (`Flush(true)`) e só então trocados.
- **Pouco espaço**: aviso ao abrir com menos de 100 MB livres; download do modelo e cópia portátil checam o espaço antes.

Testado no PC: versão portátil publicada e copiada para uma "unidade de pendrive" simulada (`subst X:`). Abriu em modo portátil,
criou configurações, perfis e logs dentro dela e carregou a biblioteca nativa do llama.cpp (recusou um `.gguf` falso com mensagem
clara). Depois, a mesma pasta como `W:`: os dados continuaram lá (perfis não foram recriados). Nenhum arquivo de
`%LocalAppData%\MaidInstaller` foi alterado durante o teste. "Criar cópia portátil" testado pela interface numa pasta local.
Na Etapa 10 o teste foi repetido no pendrive Ventoy real (D:, exFAT), com sucesso — ver [TESTES.md](TESTES.md).

## Detalhes da Etapa 10

- **Achados dos testes reais e correções**: mensagem "Atualização concluída" em atualizações; comparações e perguntas de categoria
  respondidas direto da base (o modelo misturava fatos); `AnswerGuard.Ground` remove frases da IA que não têm apoio nos fatos.
- **Ícones**: 39 PNGs oficiais em `Assets/AppIcons` (`BundledIcons`), usados quando o app não está instalado — ver [ICONES.md](ICONES.md).
- Matriz completa e limitações em [TESTES.md](TESTES.md).


## Detalhes da Etapa 11

- **Release** (`build-release.ps1`): roda os testes (para se algum falhar), publica Release win-x64 self-contained e gera em `dist\release`:
  `MaidInstaller-<versão>-portatil-win-x64.zip`, `MaidInstaller-<versão>-win-x64.zip` (cerca de 104 MB cada), `SHA256SUMS.txt` e `release.json`.
  Nenhum dado pessoal vai nos pacotes (sem configurações, listas, perfis, logs nem modelo de IA).
- **Instalável** (`installer/`): `Instalar.cmd` → `Instalar.ps1` copia para `%LocalAppData%\Programs\MaidInstaller` e cria o atalho no
  Menu Iniciar (Área de Trabalho opcional). Sem administrador e sem registro. Recusa pastas que não são do Maid Installer, cópias portáteis
  e o app aberto. `Desinstalar.ps1` remove programa e atalhos; os dados só saem com resposta "S" específica.
- **Configuração padrão**: `Defaults/settings.default.json` (com comentários), lido só na primeira execução; inválido → padrões internos.
- **Futuras atualizações**: `schemaVersion` nunca rebaixado, campos desconhecidos preservados (`ExtraFields`), ponto único de migração
  (`SettingsService.Migrate`), `CHANGELOG.md` e roteiro de lançamento em [ATUALIZACOES.md](ATUALIZACOES.md). O app não se atualiza sozinho.

Testado no PC: os dois `.zip` extraídos com `Expand-Archive`. O portátil abriu em modo portátil, leu a configuração padrão, criou os
perfis e não gravou nada em `%LocalAppData%`. O instalável: instalação, abertura pelo atalho (modo instalado com os dados existentes e
a IA), atualização recusada com o app aberto e feita com ele fechado (perfis mantidos), desinstalação (dados mantidos, 11/11 arquivos)
e recusa de pasta de destino alheia (arquivo preservado).
