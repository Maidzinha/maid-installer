using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.PostFormat;

namespace MaidInstaller.App.ViewModels;

public enum KitLevel { Unknown, Busy, Ok, Info, Warning }

public sealed record KitAction(string Label, ICommand Command, bool Primary = false, string? Tooltip = null);

/// <summary>Um cartão do Kit: verificação real (somente leitura) + ações opcionais, cada uma independente.</summary>
public sealed class KitCardViewModel(string glyph, string title, string description) : ObservableObject
{
    private string _status = L.T("Ainda não verificado");
    private KitLevel _level = KitLevel.Unknown;
    private bool _isBusy;
    private object? _selectedOption;

    public string Glyph { get; } = glyph;
    public string Title { get; } = title;
    public string Description { get; } = description;
    public ObservableCollection<string> Details { get; } = [];
    public ObservableCollection<KitAction> Actions { get; } = [];

    /// <summary>Usado só pelo cartão de energia (lista de planos existentes).</summary>
    public ObservableCollection<object> Options { get; } = [];
    public bool HasOptions => Options.Count > 0;
    public object? SelectedOption { get => _selectedOption; set => SetProperty(ref _selectedOption, value); }

    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public KitLevel Level { get => _level; set => SetProperty(ref _level, value); }
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested(); } }
    public Func<Task>? Check { get; set; }

    public void SetDetails(IEnumerable<string> lines, int max = 8)
    {
        Details.Clear();
        var all = lines.ToList();
        foreach (var l in all.Take(max)) Details.Add(l);
        if (all.Count > max) Details.Add(L.T($"… e mais {all.Count - max}"));
    }

    public void NotifyOptions() => OnPropertyChanged(nameof(HasOptions));

    public async Task RunCheckAsync(Action<Exception> onError)
    {
        if (Check is null || IsBusy) return;
        IsBusy = true;
        Level = KitLevel.Busy;
        Status = L.T("Verificando…");
        try { await Check(); }
        catch (Exception ex)
        {
            onError(ex);
            Level = KitLevel.Unknown;
            Status = L.T("Não foi possível verificar agora (detalhes nos logs).");
        }
        finally { IsBusy = false; }
    }
}

/// <summary>
/// Kit Pós-Formatação. Regras: as verificações só leem; qualquer alteração pede confirmação mostrando o que muda;
/// nada é feito em silêncio; nenhum componente do sistema é removido; o PC nunca é reiniciado.
/// </summary>
public sealed class PostFormatViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private bool _checkedOnce;

    private static readonly string[] ComponentIds = ["vcredist-x64", "vcredist-x86", "directx"];
    private static readonly string[] RuntimeIds = ["dotnet-desktop-8", "dotnet-desktop-10", "java-temurin-21"];

    public PostFormatViewModel(AppServices services, MainViewModel main)
    {
        _services = services;
        _main = main;
        Cards = [WindowsUpdate(), WinGet(), Apps("\uE74C", L.T("Componentes necessários"),
                    L.T("Bibliotecas que muitos programas e jogos precisam para abrir: Visual C++ (64 e 32 bits) e DirectX antigo."), ComponentIds),
                 Apps("\uE943", L.T("Runtimes"),
                    L.T(".NET Desktop Runtime 8 e 10 (programas feitos em .NET) e Java (programas Java e mods de Minecraft)."), RuntimeIds),
                 Drivers(), Temp(), Power(), Startup(), Extensions(), Tools()];
        CheckAllCommand = new RelayCommand(() => _ = CheckAllAsync(), () => Cards.All(c => !c.IsBusy));
    }

    public IReadOnlyList<KitCardViewModel> Cards { get; }

    /// <summary>Duas colunas independentes (cada cartão tem a altura do próprio conteúdo).</summary>
    public IEnumerable<KitCardViewModel> LeftCards => Cards.Where((_, i) => i % 2 == 0);
    public IEnumerable<KitCardViewModel> RightCards => Cards.Where((_, i) => i % 2 == 1);
    public ICommand CheckAllCommand { get; }

    /// <summary>Chamado ao abrir a página: faz as verificações rápidas uma vez.</summary>
    public void OnShown()
    {
        if (_checkedOnce) return;
        _checkedOnce = true;
        _ = CheckAllAsync();
    }

    private async Task CheckAllAsync()
    {
        _services.Logger.Info("Kit Pós-Formatação: verificando tudo.");
        await Task.WhenAll(Cards.Select(c => c.RunCheckAsync(LogError)));
    }

    private void LogError(Exception ex) => _services.Logger.Error("Kit Pós-Formatação: falha numa verificação.", ex);

    private RelayCommand Cmd(KitCardViewModel card, Func<Task> action) =>
        new(() => _ = Guard(card, action), () => !card.IsBusy);

    private async Task Guard(KitCardViewModel card, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            _services.Logger.Error($"Kit: falha em {card.Title}.", ex);
            card.Status = L.T("Algo deu errado. Os detalhes estão nos logs.");
            card.Level = KitLevel.Warning;
            card.IsBusy = false;
        }
    }

    private RelayCommand OpenCmd(string target, string? args = null) => new(() =>
    {
        try
        {
            SystemTool.Open(target, args);
            _services.Logger.Info($"Kit: abrindo {target} {args}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _services.Logger.Warn($"Kit: não foi possível abrir {target}: {ex.Message}");
        }
    });


    // ---------- 1. Windows Update ----------
    private KitCardViewModel WindowsUpdate()
    {
        var card = new KitCardViewModel("\uE895", L.T("Atualizações do Windows"),
            L.T("Mostra a última atualização instalada e procura as pendentes pela API oficial do Windows Update. A instalação fica com o próprio Windows Update — o Maid Installer não reinicia o PC."));
        card.Check = async () =>
        {
            var installed = await Task.Run(WindowsUpdateCheck.GetInstalled);
            var last = installed.FirstOrDefault(u => u.InstalledOn is not null);
            card.Status = last is null ? L.T("Não consegui ler o histórico de atualizações.") : L.T($"Última atualização instalada em {last.InstalledOn:dd/MM/yyyy} ({last.Id})");
            card.Level = last is null ? KitLevel.Unknown
                : (DateTime.Now - last.InstalledOn!.Value).TotalDays > 45 ? KitLevel.Warning : KitLevel.Ok;
            card.SetDetails(installed.Take(4).Select(u => $"{u.Id} · {u.Kind ?? L.T("Atualização")} · {u.InstalledOn:dd/MM/yyyy}"));
        };
        card.Actions.Add(new KitAction(L.T("Procurar atualizações"), Cmd(card, async () =>
        {
            card.IsBusy = true;
            card.Level = KitLevel.Busy;
            card.Status = L.T("Procurando no Windows Update… (pode levar alguns minutos)");
            _services.Logger.Info("Kit: procurando atualizações do Windows.");
            var pending = await WindowsUpdateCheck.SearchPendingAsync();
            card.IsBusy = false;
            card.Status = pending.Count == 0 ? L.T("Nenhuma atualização pendente. ♡") : L.T($"{L.Plural(pending.Count, "atualização pendente", "atualizações pendentes")} — instale pelo Windows Update.");
            card.Level = pending.Count == 0 ? KitLevel.Ok : KitLevel.Warning;
            card.SetDetails(pending.Select(p => p.Title + (p.IsDownloaded ? L.T(" (já baixada)") : "")));
            _services.Logger.Info($"Kit: {pending.Count} atualizações pendentes do Windows.");
        }), Primary: true, L.T("Só procura; não instala nada")));
        card.Actions.Add(new KitAction(L.T("Abrir Windows Update"), OpenCmd(AdminTools.Settings.WindowsUpdate)));
        return card;
    }

    // ---------- 2. WinGet ----------
    private KitCardViewModel WinGet()
    {
        var card = new KitCardViewModel("\uE7B8", "WinGet",
            L.T("O gerenciador de pacotes oficial da Microsoft, que o Maid Installer usa para instalar tudo. Vem no \"Instalador de Aplicativo\" da Microsoft Store."));
        card.Check = async () =>
        {
            var version = await _services.WinGet.GetVersionAsync();
            card.Status = version is null ? L.T("WinGet não encontrado neste PC.") : L.T($"Instalado · {version}");
            card.Level = version is null ? KitLevel.Warning : KitLevel.Ok;
            card.SetDetails(version is null
                ? [L.T("Abra a página do Instalador de Aplicativo na Microsoft Store e clique em Obter/Atualizar.")]
                : [L.T("Pronto para instalar apps da Biblioteca, perfis e listas.")]);
        };
        card.Actions.Add(new KitAction(L.T("Abrir na Microsoft Store"), OpenCmd(AdminTools.Settings.AppInstallerStore), Tooltip: L.T("Página oficial do Instalador de Aplicativo")));
        return card;
    }

    // ---------- 3/4. Componentes e runtimes (via catálogo e fila de instalação) ----------
    private KitCardViewModel Apps(string glyph, string title, string description, string[] ids)
    {
        var card = new KitCardViewModel(glyph, title, description);
        card.Check = async () =>
        {
            var library = _main.Library;
            await library.WaitForStatusAsync();
            var items = ids.Select(library.Find).OfType<AppItemViewModel>().ToList();
            var found = await Task.Run(() => items.ToDictionary(i => i.App.Id, i => i.IsInstalled ? null : RuntimeDetector.For(i.App.Id)?.Invoke()));
            var notFound = items.Where(i => !i.IsInstalled && found[i.App.Id] is null).ToList();
            var unverified = notFound.Count(i => i.Status.State == InstallState.NotVerified);
            var missing = notFound.Count - unverified;
            card.Status = (missing, unverified) switch
            {
                (0, 0) => L.T("Tudo instalado ♡"),
                (0, _) => L.T($"{L.Plural(unverified, "item não verificado", "itens não verificados")} (sem WinGet)"),
                (_, 0) => L.T($"{L.Plural(missing, "item faltando", "itens faltando")} de {items.Count}"),
                _ => L.T($"{L.Plural(missing, "item faltando", "itens faltando")} · {unverified} não verificado(s) (sem WinGet)"),
            };
            card.Level = missing == 0 && unverified == 0 ? KitLevel.Ok : unverified > 0 && missing == 0 ? KitLevel.Unknown : KitLevel.Info;
            card.SetDetails(items.Select(i => $"{i.App.Name} — " +
                (found[i.App.Id] is { } v ? L.T($"Instalado (encontrado no PC · {v})") : i.StatusText)));
        };
        card.Actions.Add(new KitAction(L.T("Instalar os que faltam"), Cmd(card, async () =>
        {
            var library = _main.Library;
            var items = ids.Select(library.Find).OfType<AppItemViewModel>().ToList();
            var installed = await Task.Run(() => items
                .Where(i => i.IsInstalled || RuntimeDetector.For(i.App.Id)?.Invoke() is not null)
                .Select(i => i.App.Id).ToHashSet());
            await _main.RequestInstallAsync(items.Select(i => i.App), a => installed.Contains(a.Id));
        }), Primary: true, L.T("Mostra a confirmação antes de instalar")));
        return card;
    }

    // ---------- 5. Drivers ----------
    private KitCardViewModel Drivers()
    {
        var card = new KitCardViewModel("\uE7F4", L.T("Drivers"),
            L.T("Procura dispositivos com problema no Gerenciador de Dispositivos e mostra a versão do driver de vídeo. Drivers novos vêm do Windows Update (atualizações opcionais) ou do site do fabricante."));
        card.Check = async () =>
        {
            var report = await Task.Run(DriverCheck.Run);
            if (!report.Supported) { card.Status = L.T("Não foi possível consultar os dispositivos."); card.Level = KitLevel.Unknown; return; }
            var real = report.Problems.Where(p => !p.IsDisabledByUser).ToList();
            card.Status = real.Count == 0 ? L.T("Nenhum dispositivo com problema ♡") : $"{L.Plural(real.Count, "dispositivo com problema", "dispositivos com problema")}";
            card.Level = real.Count == 0 ? KitLevel.Ok : KitLevel.Warning;
            card.SetDetails([
                .. report.Video.Select(v => L.T($"Vídeo: {v.Name} · driver {v.Version ?? "?"}") + (v.Date is { } d ? $" ({d:dd/MM/yyyy})" : "")),
                .. real.Select(p => $"⚠ {p.Name}: {p.Explanation}"),
                .. report.Problems.Where(p => p.IsDisabledByUser).Select(p => $"{p.Name}: {p.Explanation}"),
            ]);
        };
        card.Actions.Add(new KitAction(L.T("Gerenciador de Dispositivos"), OpenCmd("devmgmt.msc"), Primary: true));
        card.Actions.Add(new KitAction(L.T("Drivers no Windows Update"), OpenCmd(AdminTools.Settings.OptionalUpdates), Tooltip: L.T("Atualizações opcionais → Atualizações de driver")));
        return card;
    }

    // ---------- 7. Arquivos temporários ----------
    private KitCardViewModel Temp()
    {
        var dir = TempCleaner.UserTempDirectory;
        var card = new KitCardViewModel("\uE74D", L.T("Arquivos temporários"),
            L.T($"Apaga da sua pasta temporária ({dir}) só arquivos sem uso há mais de 24 horas. Arquivos em uso, protegidos ou fora dessa pasta nunca são tocados."));
        card.Check = async () =>
        {
            var scan = await Task.Run(() => TempCleaner.Scan(dir, TempCleaner.DefaultMinAge));
            card.Status = scan.EligibleFiles == 0 ? L.T("Nada para limpar ♡") : L.T($"{TempCleaner.FormatBytes(scan.EligibleBytes)} podem ser liberados");
            card.Level = scan.EligibleBytes > 500L * 1024 * 1024 ? KitLevel.Warning : scan.EligibleFiles == 0 ? KitLevel.Ok : KitLevel.Info;
            card.SetDetails([
                L.T($"Total na pasta: {L.Plural(scan.TotalFiles, "arquivo", "arquivos")} · {TempCleaner.FormatBytes(scan.TotalBytes)}"),
                L.T($"Antigos (mais de 24 h): {L.Plural(scan.EligibleFiles, "arquivo", "arquivos")} · {TempCleaner.FormatBytes(scan.EligibleBytes)}"),
            ]);
        };
        card.Actions.Add(new KitAction(L.T("Limpar"), Cmd(card, async () =>
        {
            var scan = await Task.Run(() => TempCleaner.Scan(dir, TempCleaner.DefaultMinAge));
            if (scan.EligibleFiles == 0) { card.Status = L.T("Nada para limpar ♡"); card.Level = KitLevel.Ok; return; }
            var ok = await _main.ShowDialogAsync(new DialogViewModel(L.T("Limpar arquivos temporários?"),
                L.T($"Serão apagados até {L.Plural(scan.EligibleFiles, "arquivo", "arquivos")} ({TempCleaner.FormatBytes(scan.EligibleBytes)}) de:"),
                [new DialogItem(dir, L.T("Somente arquivos sem uso há mais de 24 horas"))],
                [L.T("Arquivos temporários são descartáveis, mas apagados não voltam (não vão para a Lixeira)."),
                 L.T("Arquivos em uso ou somente leitura são pulados."),
                 L.T("A pasta do sistema (C:\\Windows\\Temp) não é tocada — para ela, use a Limpeza de Disco do Windows.")],
                L.T("Limpar")));
            if (!ok) return;
            card.IsBusy = true;
            card.Level = KitLevel.Busy;
            card.Status = L.T("Limpando…");
            _services.Logger.Info($"Kit: limpando temporários em {dir}.");
            var r = await Task.Run(() => TempCleaner.Clean(dir, TempCleaner.DefaultMinAge));
            _services.Logger.Info($"Kit: temporários — {r.DeletedFiles} arquivos ({r.FreedBytes} bytes) apagados, {r.SkippedFiles} pulados, {r.DeletedFolders} pastas vazias.");
            card.IsBusy = false;
            card.Status = L.T($"Liberado {TempCleaner.FormatBytes(r.FreedBytes)} ♡");
            card.Level = KitLevel.Ok;
            card.SetDetails([$"{L.Plural(r.DeletedFiles, "arquivo apagado", "arquivos apagados")}", L.T($"{L.Plural(r.SkippedFiles, "arquivo pulado", "arquivos pulados")} (em uso ou protegidos)")]);
            _main.Assistant.Post(L.T($"Limpei os temporários, {L.Name(_services.Settings.UserName)}: liberei {TempCleaner.FormatBytes(r.FreedBytes)}. ♡"), expand: false);
        }), Primary: true, L.T("Mostra o que será apagado antes")));
        card.Actions.Add(new KitAction(L.T("Sensor de Armazenamento"), OpenCmd(AdminTools.Settings.StorageSense)));
        return card;
    }

    // ---------- 8. Energia ----------
    private KitCardViewModel Power()
    {
        var card = new KitCardViewModel("\uE945", L.T("Opções de energia"),
            L.T("Mostra o plano de energia ativo e permite trocar por outro plano que já existe no PC (\"Alto desempenho\" para jogos, por exemplo). Nenhum plano é criado ou apagado."));
        card.Check = async () =>
        {
            var plans = await PowerPlans.GetAsync();
            card.Options.Clear();
            foreach (var p in plans) card.Options.Add(p);
            card.NotifyOptions();
            var active = plans.FirstOrDefault(p => p.IsActive);
            card.SelectedOption = active;
            card.Status = active is null ? L.T("Não consegui ler os planos de energia.") : L.T($"Plano ativo: {active.Name}");
            card.Level = active is null ? KitLevel.Unknown : KitLevel.Ok;
            card.Details.Clear();
        };
        card.Actions.Add(new KitAction(L.T("Ativar plano escolhido"), Cmd(card, async () =>
        {
            if (card.SelectedOption is not PowerPlan plan) return;
            var current = card.Options.OfType<PowerPlan>().FirstOrDefault(p => p.IsActive);
            if (plan.IsActive) { card.Status = L.T($"\"{plan.Name}\" já é o plano ativo."); return; }
            var ok = await _main.ShowDialogAsync(new DialogViewModel($"Ativar \"{plan.Name}\"?",
                L.T("O plano de energia do Windows será trocado:"),
                [new DialogItem($"{current?.Name ?? "?"} → {plan.Name}", L.T("powercfg /setactive (mesma opção do Painel de Controle)"))],
                [L.T("Você pode voltar ao plano anterior aqui mesmo, a qualquer momento."), L.T("Planos de mais desempenho gastam mais energia (em notebooks, mais bateria).")],
                L.T("Ativar")));
            if (!ok) return;
            _services.Logger.Info($"Kit: trocando plano de energia {current?.Name} → {plan.Name} ({plan.Id}).");
            var done = await PowerPlans.SetActiveAsync(plan.Id);
            await card.RunCheckAsync(LogError);
            if (!done) { card.Status = L.T("O Windows não aceitou a troca do plano."); card.Level = KitLevel.Warning; }
            else card.Details.Add(L.T($"Plano anterior: {current?.Name}"));
        }), Primary: true));
        card.Actions.Add(new KitAction(L.T("Abrir opções de energia"), OpenCmd("control.exe", "powercfg.cpl")));
        return card;
    }

    // ---------- 9. Inicialização ----------
    private KitCardViewModel Startup()
    {
        var card = new KitCardViewModel("\uE7E8", L.T("Programas de inicialização"),
            L.T("Lista os programas que abrem junto com o Windows (registro e pasta Inicializar). Só consulta: para ativar ou desativar, use o Gerenciador de Tarefas ou as Configurações."));
        card.Check = async () =>
        {
            var list = await Task.Run(StartupPrograms.Read);
            var enabled = list.Count(e => e.Enabled != false);
            card.Status = L.T($"{L.Plural(enabled, "programa ativo", "programas ativos")} · {list.Count - enabled} desativado(s)");
            card.Level = enabled > 12 ? KitLevel.Warning : KitLevel.Info;
            card.SetDetails(list.OrderBy(e => e.Enabled == false).Select(e => $"{(e.Enabled == false ? "○" : "●")} {e.Name} · {e.Location}"), max: 10);
        };
        card.Actions.Add(new KitAction(L.T("Gerenciador de Tarefas"), OpenCmd("taskmgr.exe", "/0 /startup"), Primary: true, L.T("Aba Aplicativos de inicialização")));
        card.Actions.Add(new KitAction(L.T("Configurações"), OpenCmd(AdminTools.Settings.StartupApps)));
        return card;
    }

    // ---------- 10. Extensões de arquivos ----------
    private KitCardViewModel Extensions()
    {
        var card = new KitCardViewModel("\uE8A5", L.T("Extensões de arquivos"),
            L.T("Mostra ou esconde a extensão no nome dos arquivos (ex.: foto.jpg, setup.exe). Ver a extensão ajuda a evitar arquivos disfarçados. Também abre os Aplicativos padrão do Windows."));
        card.Check = () =>
        {
            var visible = FileExtensions.AreVisible();
            card.Status = visible ? L.T("As extensões estão visíveis ✓") : L.T("As extensões estão escondidas");
            card.Level = visible ? KitLevel.Ok : KitLevel.Info;
            card.Details.Clear();
            return Task.CompletedTask;
        };
        card.Actions.Add(new KitAction(L.T("Mostrar/esconder extensões"), Cmd(card, async () =>
        {
            var visible = FileExtensions.AreVisible();
            var ok = await _main.ShowDialogAsync(new DialogViewModel(visible ? L.T("Esconder as extensões?") : L.T("Mostrar as extensões?"),
                L.T("Configuração do Explorador de Arquivos (só para o seu usuário):"),
                [new DialogItem(L.T("Ocultar as extensões dos tipos de arquivo conhecidos"), visible ? L.T("Desligado → Ligado") : L.T("Ligado → Desligado"))],
                [L.T("É a mesma opção do menu Exibir do Explorador. Dá para desfazer aqui ou lá.")], visible ? L.T("Esconder") : L.T("Mostrar")));
            if (!ok) return;
            FileExtensions.SetVisible(!visible);
            _services.Logger.Info($"Kit: extensões de arquivo {(visible ? "escondidas" : "visíveis")}.");
            await card.RunCheckAsync(LogError);
        }), Primary: true));
        card.Actions.Add(new KitAction(L.T("Aplicativos padrão"), OpenCmd(AdminTools.Settings.DefaultApps), Tooltip: L.T("Escolher qual programa abre cada tipo de arquivo")));
        return card;
    }

    // ---------- 6. Ferramentas administrativas ----------
    private KitCardViewModel Tools()
    {
        var card = new KitCardViewModel("\uE90F", L.T("Ferramentas administrativas"),
            L.T("Atalhos para as ferramentas do próprio Windows. Abrir não altera nada; algumas pedem permissão de administrador (UAC)."));
        card.Status = L.T("Escolha uma ferramenta");
        card.Level = KitLevel.Info;
        foreach (var t in AdminTools.All)
            card.Actions.Add(new KitAction(L.T(t.Name), OpenCmd(t.Target, t.Arguments), Tooltip: L.T(t.Description) + (t.AsksAdmin ? L.T(" · pede administrador") : "")));
        return card;
    }
}
