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
    private string _status = "Ainda não verificado";
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
        if (all.Count > max) Details.Add($"… e mais {all.Count - max}");
    }

    public void NotifyOptions() => OnPropertyChanged(nameof(HasOptions));

    public async Task RunCheckAsync(Action<Exception> onError)
    {
        if (Check is null || IsBusy) return;
        IsBusy = true;
        Level = KitLevel.Busy;
        Status = "Verificando…";
        try { await Check(); }
        catch (Exception ex)
        {
            onError(ex);
            Level = KitLevel.Unknown;
            Status = "Não foi possível verificar agora (detalhes nos logs).";
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
        Cards = [WindowsUpdate(), WinGet(), Apps("\uE74C", "Componentes necessários",
                    "Bibliotecas que muitos programas e jogos precisam para abrir: Visual C++ (64 e 32 bits) e DirectX antigo.", ComponentIds),
                 Apps("\uE943", "Runtimes",
                    ".NET Desktop Runtime 8 e 10 (programas feitos em .NET) e Java (programas Java e mods de Minecraft).", RuntimeIds),
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
            card.Status = "Algo deu errado. Os detalhes estão nos logs.";
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

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    // ---------- 1. Windows Update ----------
    private KitCardViewModel WindowsUpdate()
    {
        var card = new KitCardViewModel("\uE895", "Atualizações do Windows",
            "Mostra a última atualização instalada e procura as pendentes pela API oficial do Windows Update. A instalação fica com o próprio Windows Update — o Maid Installer não reinicia o PC.");
        card.Check = async () =>
        {
            var installed = await Task.Run(WindowsUpdateCheck.GetInstalled);
            var last = installed.FirstOrDefault(u => u.InstalledOn is not null);
            card.Status = last is null ? "Não consegui ler o histórico de atualizações." : $"Última atualização instalada em {last.InstalledOn:dd/MM/yyyy} ({last.Id})";
            card.Level = last is null ? KitLevel.Unknown
                : (DateTime.Now - last.InstalledOn!.Value).TotalDays > 45 ? KitLevel.Warning : KitLevel.Ok;
            card.SetDetails(installed.Take(4).Select(u => $"{u.Id} · {u.Kind ?? "Atualização"} · {u.InstalledOn:dd/MM/yyyy}"));
        };
        card.Actions.Add(new KitAction("Procurar atualizações", Cmd(card, async () =>
        {
            card.IsBusy = true;
            card.Level = KitLevel.Busy;
            card.Status = "Procurando no Windows Update… (pode levar alguns minutos)";
            _services.Logger.Info("Kit: procurando atualizações do Windows.");
            var pending = await WindowsUpdateCheck.SearchPendingAsync();
            card.IsBusy = false;
            card.Status = pending.Count == 0 ? "Nenhuma atualização pendente. ♡" : $"{Plural(pending.Count, "atualização pendente", "atualizações pendentes")} — instale pelo Windows Update.";
            card.Level = pending.Count == 0 ? KitLevel.Ok : KitLevel.Warning;
            card.SetDetails(pending.Select(p => p.Title + (p.IsDownloaded ? " (já baixada)" : "")));
            _services.Logger.Info($"Kit: {pending.Count} atualizações pendentes do Windows.");
        }), Primary: true, "Só procura; não instala nada"));
        card.Actions.Add(new KitAction("Abrir Windows Update", OpenCmd(AdminTools.Settings.WindowsUpdate)));
        return card;
    }

    // ---------- 2. WinGet ----------
    private KitCardViewModel WinGet()
    {
        var card = new KitCardViewModel("\uE7B8", "WinGet",
            "O gerenciador de pacotes oficial da Microsoft, que o Maid Installer usa para instalar tudo. Vem no \"Instalador de Aplicativo\" da Microsoft Store.");
        card.Check = async () =>
        {
            var version = await _services.WinGet.GetVersionAsync();
            card.Status = version is null ? "WinGet não encontrado neste PC." : $"Instalado · {version}";
            card.Level = version is null ? KitLevel.Warning : KitLevel.Ok;
            card.SetDetails(version is null
                ? ["Abra a página do Instalador de Aplicativo na Microsoft Store e clique em Obter/Atualizar."]
                : ["Pronto para instalar apps da Biblioteca, perfis e listas."]);
        };
        card.Actions.Add(new KitAction("Abrir na Microsoft Store", OpenCmd(AdminTools.Settings.AppInstallerStore), Tooltip: "Página oficial do Instalador de Aplicativo"));
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
                (0, 0) => "Tudo instalado ♡",
                (0, _) => $"{Plural(unverified, "item não verificado", "itens não verificados")} (sem WinGet)",
                (_, 0) => $"{Plural(missing, "item faltando", "itens faltando")} de {items.Count}",
                _ => $"{Plural(missing, "item faltando", "itens faltando")} · {unverified} não verificado(s) (sem WinGet)",
            };
            card.Level = missing == 0 && unverified == 0 ? KitLevel.Ok : unverified > 0 && missing == 0 ? KitLevel.Unknown : KitLevel.Info;
            card.SetDetails(items.Select(i => $"{i.App.Name} — " +
                (found[i.App.Id] is { } v ? $"Instalado (encontrado no PC · {v})" : i.StatusText)));
        };
        card.Actions.Add(new KitAction("Instalar os que faltam", Cmd(card, async () =>
        {
            var library = _main.Library;
            var items = ids.Select(library.Find).OfType<AppItemViewModel>().ToList();
            var installed = await Task.Run(() => items
                .Where(i => i.IsInstalled || RuntimeDetector.For(i.App.Id)?.Invoke() is not null)
                .Select(i => i.App.Id).ToHashSet());
            await _main.RequestInstallAsync(items.Select(i => i.App), a => installed.Contains(a.Id));
        }), Primary: true, "Mostra a confirmação antes de instalar"));
        return card;
    }

    // ---------- 5. Drivers ----------
    private KitCardViewModel Drivers()
    {
        var card = new KitCardViewModel("\uE7F4", "Drivers",
            "Procura dispositivos com problema no Gerenciador de Dispositivos e mostra a versão do driver de vídeo. Drivers novos vêm do Windows Update (atualizações opcionais) ou do site do fabricante.");
        card.Check = async () =>
        {
            var report = await Task.Run(DriverCheck.Run);
            if (!report.Supported) { card.Status = "Não foi possível consultar os dispositivos."; card.Level = KitLevel.Unknown; return; }
            var real = report.Problems.Where(p => !p.IsDisabledByUser).ToList();
            card.Status = real.Count == 0 ? "Nenhum dispositivo com problema ♡" : $"{Plural(real.Count, "dispositivo com problema", "dispositivos com problema")}";
            card.Level = real.Count == 0 ? KitLevel.Ok : KitLevel.Warning;
            card.SetDetails([
                .. report.Video.Select(v => $"Vídeo: {v.Name} · driver {v.Version ?? "?"}" + (v.Date is { } d ? $" ({d:dd/MM/yyyy})" : "")),
                .. real.Select(p => $"⚠ {p.Name}: {p.Explanation}"),
                .. report.Problems.Where(p => p.IsDisabledByUser).Select(p => $"{p.Name}: {p.Explanation}"),
            ]);
        };
        card.Actions.Add(new KitAction("Gerenciador de Dispositivos", OpenCmd("devmgmt.msc"), Primary: true));
        card.Actions.Add(new KitAction("Drivers no Windows Update", OpenCmd(AdminTools.Settings.OptionalUpdates), Tooltip: "Atualizações opcionais → Atualizações de driver"));
        return card;
    }

    // ---------- 7. Arquivos temporários ----------
    private KitCardViewModel Temp()
    {
        var dir = TempCleaner.UserTempDirectory;
        var card = new KitCardViewModel("\uE74D", "Arquivos temporários",
            $"Apaga da sua pasta temporária ({dir}) só arquivos sem uso há mais de 24 horas. Arquivos em uso, protegidos ou fora dessa pasta nunca são tocados.");
        card.Check = async () =>
        {
            var scan = await Task.Run(() => TempCleaner.Scan(dir, TempCleaner.DefaultMinAge));
            card.Status = scan.EligibleFiles == 0 ? "Nada para limpar ♡" : $"{TempCleaner.FormatBytes(scan.EligibleBytes)} podem ser liberados";
            card.Level = scan.EligibleBytes > 500L * 1024 * 1024 ? KitLevel.Warning : scan.EligibleFiles == 0 ? KitLevel.Ok : KitLevel.Info;
            card.SetDetails([
                $"Total na pasta: {Plural(scan.TotalFiles, "arquivo", "arquivos")} · {TempCleaner.FormatBytes(scan.TotalBytes)}",
                $"Antigos (mais de 24 h): {Plural(scan.EligibleFiles, "arquivo", "arquivos")} · {TempCleaner.FormatBytes(scan.EligibleBytes)}",
            ]);
        };
        card.Actions.Add(new KitAction("Limpar", Cmd(card, async () =>
        {
            var scan = await Task.Run(() => TempCleaner.Scan(dir, TempCleaner.DefaultMinAge));
            if (scan.EligibleFiles == 0) { card.Status = "Nada para limpar ♡"; card.Level = KitLevel.Ok; return; }
            var ok = await _main.ShowDialogAsync(new DialogViewModel("Limpar arquivos temporários?",
                $"Serão apagados até {Plural(scan.EligibleFiles, "arquivo", "arquivos")} ({TempCleaner.FormatBytes(scan.EligibleBytes)}) de:",
                [new DialogItem(dir, "Somente arquivos sem uso há mais de 24 horas")],
                ["Arquivos temporários são descartáveis, mas apagados não voltam (não vão para a Lixeira).",
                 "Arquivos em uso ou somente leitura são pulados.",
                 "A pasta do sistema (C:\\Windows\\Temp) não é tocada — para ela, use a Limpeza de Disco do Windows."],
                "Limpar"));
            if (!ok) return;
            card.IsBusy = true;
            card.Level = KitLevel.Busy;
            card.Status = "Limpando…";
            _services.Logger.Info($"Kit: limpando temporários em {dir}.");
            var r = await Task.Run(() => TempCleaner.Clean(dir, TempCleaner.DefaultMinAge));
            _services.Logger.Info($"Kit: temporários — {r.DeletedFiles} arquivos ({r.FreedBytes} bytes) apagados, {r.SkippedFiles} pulados, {r.DeletedFolders} pastas vazias.");
            card.IsBusy = false;
            card.Status = $"Liberado {TempCleaner.FormatBytes(r.FreedBytes)} ♡";
            card.Level = KitLevel.Ok;
            card.SetDetails([$"{Plural(r.DeletedFiles, "arquivo apagado", "arquivos apagados")}", $"{Plural(r.SkippedFiles, "arquivo pulado", "arquivos pulados")} (em uso ou protegidos)"]);
            _main.Assistant.Post($"Limpei os temporários, {_services.Settings.UserName}: liberei {TempCleaner.FormatBytes(r.FreedBytes)}. ♡", expand: false);
        }), Primary: true, "Mostra o que será apagado antes"));
        card.Actions.Add(new KitAction("Sensor de Armazenamento", OpenCmd(AdminTools.Settings.StorageSense)));
        return card;
    }

    // ---------- 8. Energia ----------
    private KitCardViewModel Power()
    {
        var card = new KitCardViewModel("\uE945", "Opções de energia",
            "Mostra o plano de energia ativo e permite trocar por outro plano que já existe no PC (\"Alto desempenho\" para jogos, por exemplo). Nenhum plano é criado ou apagado.");
        card.Check = async () =>
        {
            var plans = await PowerPlans.GetAsync();
            card.Options.Clear();
            foreach (var p in plans) card.Options.Add(p);
            card.NotifyOptions();
            var active = plans.FirstOrDefault(p => p.IsActive);
            card.SelectedOption = active;
            card.Status = active is null ? "Não consegui ler os planos de energia." : $"Plano ativo: {active.Name}";
            card.Level = active is null ? KitLevel.Unknown : KitLevel.Ok;
            card.Details.Clear();
        };
        card.Actions.Add(new KitAction("Ativar plano escolhido", Cmd(card, async () =>
        {
            if (card.SelectedOption is not PowerPlan plan) return;
            var current = card.Options.OfType<PowerPlan>().FirstOrDefault(p => p.IsActive);
            if (plan.IsActive) { card.Status = $"\"{plan.Name}\" já é o plano ativo."; return; }
            var ok = await _main.ShowDialogAsync(new DialogViewModel($"Ativar \"{plan.Name}\"?",
                "O plano de energia do Windows será trocado:",
                [new DialogItem($"{current?.Name ?? "?"} → {plan.Name}", "powercfg /setactive (mesma opção do Painel de Controle)")],
                ["Você pode voltar ao plano anterior aqui mesmo, a qualquer momento.", "Planos de mais desempenho gastam mais energia (em notebooks, mais bateria)."],
                "Ativar"));
            if (!ok) return;
            _services.Logger.Info($"Kit: trocando plano de energia {current?.Name} → {plan.Name} ({plan.Id}).");
            var done = await PowerPlans.SetActiveAsync(plan.Id);
            await card.RunCheckAsync(LogError);
            if (!done) { card.Status = "O Windows não aceitou a troca do plano."; card.Level = KitLevel.Warning; }
            else card.Details.Add($"Plano anterior: {current?.Name}");
        }), Primary: true));
        card.Actions.Add(new KitAction("Abrir opções de energia", OpenCmd("control.exe", "powercfg.cpl")));
        return card;
    }

    // ---------- 9. Inicialização ----------
    private KitCardViewModel Startup()
    {
        var card = new KitCardViewModel("\uE7E8", "Programas de inicialização",
            "Lista os programas que abrem junto com o Windows (registro e pasta Inicializar). Só consulta: para ativar ou desativar, use o Gerenciador de Tarefas ou as Configurações.");
        card.Check = async () =>
        {
            var list = await Task.Run(StartupPrograms.Read);
            var enabled = list.Count(e => e.Enabled != false);
            card.Status = $"{Plural(enabled, "programa ativo", "programas ativos")} · {list.Count - enabled} desativado(s)";
            card.Level = enabled > 12 ? KitLevel.Warning : KitLevel.Info;
            card.SetDetails(list.OrderBy(e => e.Enabled == false).Select(e => $"{(e.Enabled == false ? "○" : "●")} {e.Name} · {e.Location}"), max: 10);
        };
        card.Actions.Add(new KitAction("Gerenciador de Tarefas", OpenCmd("taskmgr.exe", "/0 /startup"), Primary: true, "Aba Aplicativos de inicialização"));
        card.Actions.Add(new KitAction("Configurações", OpenCmd(AdminTools.Settings.StartupApps)));
        return card;
    }

    // ---------- 10. Extensões de arquivos ----------
    private KitCardViewModel Extensions()
    {
        var card = new KitCardViewModel("\uE8A5", "Extensões de arquivos",
            "Mostra ou esconde a extensão no nome dos arquivos (ex.: foto.jpg, setup.exe). Ver a extensão ajuda a evitar arquivos disfarçados. Também abre os Aplicativos padrão do Windows.");
        card.Check = () =>
        {
            var visible = FileExtensions.AreVisible();
            card.Status = visible ? "As extensões estão visíveis ✓" : "As extensões estão escondidas";
            card.Level = visible ? KitLevel.Ok : KitLevel.Info;
            card.Details.Clear();
            return Task.CompletedTask;
        };
        card.Actions.Add(new KitAction("Mostrar/esconder extensões", Cmd(card, async () =>
        {
            var visible = FileExtensions.AreVisible();
            var ok = await _main.ShowDialogAsync(new DialogViewModel(visible ? "Esconder as extensões?" : "Mostrar as extensões?",
                "Configuração do Explorador de Arquivos (só para o seu usuário):",
                [new DialogItem("Ocultar as extensões dos tipos de arquivo conhecidos", visible ? "Desligado → Ligado" : "Ligado → Desligado")],
                ["É a mesma opção do menu Exibir do Explorador. Dá para desfazer aqui ou lá."], visible ? "Esconder" : "Mostrar"));
            if (!ok) return;
            FileExtensions.SetVisible(!visible);
            _services.Logger.Info($"Kit: extensões de arquivo {(visible ? "escondidas" : "visíveis")}.");
            await card.RunCheckAsync(LogError);
        }), Primary: true));
        card.Actions.Add(new KitAction("Aplicativos padrão", OpenCmd(AdminTools.Settings.DefaultApps), Tooltip: "Escolher qual programa abre cada tipo de arquivo"));
        return card;
    }

    // ---------- 6. Ferramentas administrativas ----------
    private KitCardViewModel Tools()
    {
        var card = new KitCardViewModel("\uE90F", "Ferramentas administrativas",
            "Atalhos para as ferramentas do próprio Windows. Abrir não altera nada; algumas pedem permissão de administrador (UAC).");
        card.Status = "Escolha uma ferramenta";
        card.Level = KitLevel.Info;
        foreach (var t in AdminTools.All)
            card.Actions.Add(new KitAction(t.Name, OpenCmd(t.Target, t.Arguments), Tooltip: t.Description + (t.AsksAdmin ? " · pede administrador" : "")));
        return card;
    }
}
