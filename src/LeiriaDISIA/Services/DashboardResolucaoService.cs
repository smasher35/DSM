namespace LeiriaDISIA.Services;

/// <summary>
/// Controla a disposição do Dashboard. Existiam anteriormente duas disposições selecionáveis em
/// Administração → Aparência - FHD (1920×1080, original) e UHD (2560×1440, mais compacta: menos
/// linhas de cartões/gauges) - mas, a pedido do utilizador (que preferiu ver sempre a disposição
/// UHD, mesmo em ecrãs FHD), o Dashboard passou a usar SEMPRE a disposição UHD, em qualquer
/// computador. <see cref="UhdAtivo"/> mantém-se (fixo a true) para não obrigar a alterar
/// <see cref="Views.DashboardView"/> nem o <see cref="DashboardSnapshotService"/>, que continuam a
/// perguntar-lhe qual a disposição a usar; deixou de haver, no entanto, forma de o desligar pela
/// interface - as antigas opções "FHD"/"UHD" de Administração → Aparência foram substituídas pela
/// opção "Modo Compacto" (ver <see cref="JanelaCompactaService"/>), que resolve um problema
/// diferente (tamanho das janelas de edição em ecrãs pequenos).
/// </summary>
public static class DashboardResolucaoService
{
    /// <summary>Sempre true: o Dashboard usa sempre a disposição UHD (2560×1440, compacta) -
    /// ver o comentário da classe.</summary>
    public static bool UhdAtivo => true;
}
