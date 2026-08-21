namespace LeiriaDISIA.Services;

/// <summary>
/// Permite alternar, a partir de Administração → Aparência, entre a disposição FHD (1920×1080,
/// original) e a disposição UHD (2560×1440, compacta) do Dashboard, e recorda a preferência do
/// utilizador entre sessões (reutiliza o mesmo ficheiro settings.json do <see cref="AppSettingsService"/>).
/// Qualquer <see cref="Views.DashboardView"/> atualmente aberta subscreve o evento
/// <see cref="ResolucaoMudou"/> para se reorganizar de imediato, sem ser necessário reabrir o
/// módulo Dashboard.
/// </summary>
public static class DashboardResolucaoService
{
    /// <summary>true = UHD (2560×1440, compacta); false = FHD (1920×1080, original/validada).</summary>
    public static bool UhdAtivo { get; private set; } = AppSettingsService.DashboardResolucaoUhd;

    /// <summary>Evento disparado sempre que a resolução muda, para as instâncias abertas do
    /// Dashboard se reorganizarem de imediato.</summary>
    public static event EventHandler<bool>? ResolucaoMudou;

    /// <summary>Aplica a resolução indicada e grava a preferência para sessões futuras.</summary>
    public static void Aplicar(bool uhd)
    {
        UhdAtivo = uhd;
        AppSettingsService.DashboardResolucaoUhd = uhd;
        ResolucaoMudou?.Invoke(null, uhd);
    }
}
