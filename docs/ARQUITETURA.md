# Arquitetura — Maid Installer

## Tecnologias

| Item | Escolha | Por quê |
|---|---|---|
| Linguagem / runtime | C# · .NET 10 LTS | Pedido do projeto; suporte longo |
| Interface | **WPF** | Roda em Windows 10 e 11 sem o Windows App SDK; funciona como executável portátil (WinUI 3 exige runtime extra, o que complica o pendrive) |
| Padrão | MVVM próprio (`Mvvm/ObservableObject.cs`) | Sem dependências externas |
| Armazenamento | JSON (`System.Text.Json`), gravação atômica | Legível, fácil de importar/exportar |
| Hardware | Registro, `GetSystemTimes`, `GlobalMemoryStatusEx`, WMI (`System.Management`), `nvidia-smi` | Dados reais, sem valores fictícios |
| Instalação (Etapa 4) | WinGet | Fontes oficiais |
| IA (Etapa 8) | llama.cpp + modelo GGUF Q4 compacto (opcional) | Leve e offline |

Única dependência NuGet do app: `System.Management` (oficial da Microsoft).

## Projetos

```
MaidInstaller/
├─ MaidInstaller.slnx
├─ src/
│  ├─ MaidInstaller.Core/          Lógica de negócio (sem interface)
│  │  ├─ Storage/                  AppPaths (modo instalado/portátil), JsonStore, PortableCopy (cópia para pendrive)
│  │  ├─ Settings/                 AppSettings, SettingsService
│  │  ├─ Logging/                  FileLogger (Logs/maid-AAAAMMDD.log)
│  │  ├─ SystemInfo/               WindowsSystemMonitor (CPU, GPU, RAM, temperaturas)
│  │  ├─ Shell/                    ShellAppsFolder (programas do Menu Iniciar)
│  │  ├─ Assistant/                KnowledgeBase, KnowledgeRetriever, OfflineResponder, HybridResponder, AnswerGuard, ModelDownloader
│  │  ├─ Catalog/                  AppCatalog, CatalogValidator, InstallStatusResolver
│  │  ├─ WinGet/                   WinGetClient (export, show, upgrade, error, execução com saída em tempo real), WinGetUpgradeParser
│  │  └─ Installation/             InstallationService (fila), InstallJob, InstallHistory, WinGetOutputParser, WinGetExitCodes
│  │  ├─ Collections/              AppCollection, CollectionStore, CollectionTransfer, DefaultProfiles (perfis e listas)
│  │  ├─ PostFormat/               Verificações e ações do Kit (powercfg, %TEMP%, inicialização, extensões, drivers, Windows Update)
│  ├─ MaidInstaller.AI/           IA local: LlamaChatModel (LLamaSharp/llama.cpp, CPU, contexto 1024)
│  └─ MaidInstaller.App/           Interface WPF
│     ├─ Themes/BlackPink.xaml     Paleta e estilos
│     ├─ Views/                    Telas (XAML)
│     ├─ ViewModels/               Estado e comandos das telas
│     ├─ Services/                 Composição (AppServices), ícones do Shell, BundledIcons (ícones embutidos)
│     ├─ Assets/                   Mascote, avatar, logo, ícone; AppIcons/<id>.png (ícones oficiais do catálogo)
│     ├─ Catalog/catalog.json      Catálogo de apps (IDs WinGet verificados)
│     ├─ Catalog/knowledge.json    Base de conhecimento da assistente
│     └─ Defaults/settings.default.json  Configuração padrão (só na primeira execução)
├─ tests/MaidInstaller.Core.Tests  Testes xUnit do núcleo
├─ installer/                      Instalar.cmd / Instalar.ps1 / Desinstalar.ps1 (pacote instalável, sem admin)
├─ build-release.ps1               Entrega: testes + publicação + zips + SHA256SUMS.txt + release.json
├─ publish-portable.ps1            Só a pasta portátil
└─ docs/
```

Estrutura de dados em tempo de execução (no modo portátil, ao lado do `.exe`):

```
Data/  Config/settings.json  Catalog/  Profiles/  Lists/  Models/  Assets/  Logs/  Cache/
```

As listas e os perfis ficam em pastas de dados que a atualização do app não sobrescreve.
Os JSON têm `schemaVersion`; campos de versões mais novas são preservados ao salvar (veja [ATUALIZACOES.md](ATUALIZACOES.md)).

## Fluxo

`App.xaml.cs` detecta o modo (`AppPaths.Detect`), cria as pastas, o logger e as configurações,
monta os serviços (`AppServices`) e abre a `MainWindow` com o `MainViewModel`.
A navegação troca o `CurrentPage`; cada ViewModel tem sua View ligada por `DataTemplate`.

## Regras de segurança aplicadas no código

- O app roda como usuário comum (`app.manifest` → `asInvoker`). Ações administrativas pedirão UAC uma por vez.
- A Maid Assistant **não executa nada**: só lê a base de conhecimento e recebe avisos do app (`AssistantViewModel.Post`).
- A assistente só responde com fatos de `knowledge.json`. Sem correspondência, diz que não tem a informação.
- A IA local só gera texto: recebe um prompt com os fatos encontrados e não tem acesso a comandos, arquivos ou à fila. Respostas com números/links fora dos fatos são descartadas (`AnswerGuard.Check`); frases cujas palavras não aparecem nos fatos são removidas (`AnswerGuard.Ground`) e, se a primeira frase for inventada, vale a resposta da base. Códigos de erro, preço, desenvolvedor, links, comparações e categorias nem passam pela IA: vêm prontos da base (`OfflineResponder.DirectAnswer`).
- O modelo só é baixado do endereço fixo do Hugging Face (`Qwen/`), após confirmação, e só é usado se tamanho e SHA-256 conferirem.
- Nenhum comando vem do catálogo. `CatalogValidator` descarta IDs, fontes e links suspeitos, e o `WinGetClient` valida de novo antes de cada chamada.
- O WinGet é chamado com `ArgumentList` (sem shell), então não há como injetar comandos pelos argumentos.
- Links oficiais só abrem se forem HTTPS.
- Nada reinicia o PC.

## Padrões de código

- `Nullable` habilitado; nada de `async void`, exceto em handlers de UI.
- Trabalho lento (WMI, Shell) roda fora da thread de UI (`Task.Run` / `StaThread.Run`).
- Falhas são registradas no log e nunca derrubam o app.
- Comentários e textos da interface em português do Brasil.
- Páginas não implementadas aparecem como "Em desenvolvimento" e não fingem que funcionam.
