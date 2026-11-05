namespace MaidInstaller.Core.Installation;

public enum InstallOutcome { Success, AlreadyInstalled, Cancelled, Failed }

public sealed record ExitCodeInfo(InstallOutcome Outcome, string Message, bool RebootRequired = false, bool Known = true);

/// <summary>
/// Significado dos códigos de saída do WinGet. Os nomes e significados foram conferidos com
/// <c>winget error --input &lt;código&gt;</c> (WinGet 1.29). Códigos fora desta tabela são
/// explicados em tempo real pelo próprio <c>winget error</c>.
/// </summary>
public static class WinGetExitCodes
{
    public const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    public const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    public const int RebootRequiredToFinish = unchecked((int)0x8A150109);
    public const int RebootRequiredForInstall = unchecked((int)0x8A15010A);
    public const int RebootInitiated = unchecked((int)0x8A15010B);
    public const int CancelledByUser = unchecked((int)0x8A15010C);
    public const int OtherVersionInstalled = unchecked((int)0x8A15010D);
    public const int Downgrade = unchecked((int)0x8A15010E);
    public const int BlockedByPolicy = unchecked((int)0x8A15010F);
    public const int PackageInUse = unchecked((int)0x8A150101);
    public const int InstallInProgress = unchecked((int)0x8A150102);
    public const int FileInUse = unchecked((int)0x8A150103);
    public const int MissingDependency = unchecked((int)0x8A150104);
    public const int DiskFull = unchecked((int)0x8A150105);
    public const int InsufficientMemory = unchecked((int)0x8A150106);
    public const int NoNetwork = unchecked((int)0x8A150107);
    public const int DownloadFailed = unchecked((int)0x8A150008);
    public const int ZeroByteInstaller = unchecked((int)0x8A150086);
    public const int HashMismatch = unchecked((int)0x8A150011);
    public const int NoApplicationsFound = unchecked((int)0x8A150014);
    public const int NoApplicableInstaller = unchecked((int)0x8A150010);
    public const int MsStoreInstallFailed = unchecked((int)0x8A15001E);
    /// <summary>ERROR_CANCELLED do Windows: normalmente o aviso de administrador (UAC) foi recusado.</summary>
    public const int WindowsCancelled = unchecked((int)0x800704C7);

    public static ExitCodeInfo Describe(int code) => code switch
    {
        0 => new(InstallOutcome.Success, "Instalação concluída."),
        PackageAlreadyInstalled => new(InstallOutcome.AlreadyInstalled, "Já estava instalado. Nada foi alterado."),
        UpdateNotApplicable => new(InstallOutcome.AlreadyInstalled, "Já está na versão mais recente."),
        OtherVersionInstalled => new(InstallOutcome.AlreadyInstalled, "Outra versão deste programa já está instalada."),
        Downgrade => new(InstallOutcome.AlreadyInstalled, "Uma versão mais nova já está instalada."),
        RebootRequiredToFinish => new(InstallOutcome.Success, "Instalado. Reinicie o PC quando puder para concluir.", RebootRequired: true),
        RebootInitiated => new(InstallOutcome.Success, "O próprio instalador pediu para reiniciar o PC.", RebootRequired: true),
        RebootRequiredForInstall => new(InstallOutcome.Failed, "O Windows precisa reiniciar antes desta instalação. Reinicie e tente de novo."),
        CancelledByUser => new(InstallOutcome.Cancelled, "A instalação foi cancelada no instalador."),
        WindowsCancelled => new(InstallOutcome.Cancelled, "A permissão de administrador (UAC) foi recusada."),
        PackageInUse or FileInUse => new(InstallOutcome.Failed, "O programa está aberto. Feche-o e tente de novo."),
        InstallInProgress => new(InstallOutcome.Failed, "Outra instalação está em andamento no Windows. Tente de novo em instantes."),
        MissingDependency => new(InstallOutcome.Failed, "Falta um componente de que este programa depende."),
        DiskFull => new(InstallOutcome.Failed, "Não há espaço suficiente no disco."),
        InsufficientMemory => new(InstallOutcome.Failed, "Memória insuficiente. Feche outros programas e tente de novo."),
        NoNetwork or DownloadFailed or ZeroByteInstaller =>
            new(InstallOutcome.Failed, "Falha no download. Verifique a conexão com a internet e tente de novo."),
        HashMismatch => new(InstallOutcome.Failed,
            "O arquivo baixado não confere com o esperado (hash diferente). Por segurança, ele não foi instalado."),
        NoApplicationsFound => new(InstallOutcome.Failed, "O pacote não foi encontrado na fonte do WinGet."),
        NoApplicableInstaller => new(InstallOutcome.Failed, "Não há instalador compatível com este PC."),
        BlockedByPolicy => new(InstallOutcome.Failed, "Uma política do sistema bloqueou a instalação."),
        MsStoreInstallFailed => new(InstallOutcome.Failed, "A Microsoft Store não conseguiu instalar o app. Verifique se a Loja está funcionando."),
        _ => new(InstallOutcome.Failed, $"A instalação falhou (código {Hex(code)}).", Known: false),
    };

    public static string Hex(int code) => $"0x{unchecked((uint)code):X8}";
}
