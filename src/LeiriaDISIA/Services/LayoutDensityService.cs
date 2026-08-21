using System.IO;
using System.Windows;

namespace LeiriaDISIA.Services;

/// <summary>Perfil de tamanhos usado pelas vistas que se adaptam à resolução do ecrã.</summary>
public enum ResolucaoEcra
{
    /// <summary>1920 × 1080 (Full HD).</summary>
    FHD,

    /// <summary>2560 × 1440 (QHD/UHD) ou superior.</summary>
    UHD
}

/// <summary>
/// Permite alternar, por computador, entre um conjunto de tamanhos otimizado para ecrãs FHD
/// (1920x1080) e outro para ecrãs maiores (2560x1440 e superiores). Tal como o <see cref="ThemeService"/>,
/// a preferência é gravada localmente (não na base de dados), porque esta app é usada em dois
/// computadores diferentes e cada um pode ter o seu próprio monitor.
///
/// Os valores concretos (alturas de gráficos, tamanhos de gauges, larguras máximas de cartões,
/// etc.) não estão em código: vivem em Themes/LayoutFHD.xaml e Themes/LayoutUHD.xaml, e as vistas
/// (por agora só o Dashboard) referem-se-lhes com {DynamicResource ChaveQualquer}. Trocar de
/// perfil troca o dicionário inteiro, tal como acontece já com o tema Claro/Escuro.
/// </summary>
public static class LayoutDensityService
{
    private static readonly string CaminhoPreferencia = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LeiriaDISIA", "resolucao.txt");

    public static ResolucaoEcra ResolucaoAtual { get; private set; } = ResolucaoEcra.FHD;

    /// <summary>Evento disparado quando o perfil de layout muda.</summary>
    public static event EventHandler<ResolucaoEcra>? ResolucaoMudou;

    /// <summary>Lê a preferência gravada (se existir) e aplica-a. A chamar uma vez, no arranque da aplicação.</summary>
    public static void AplicarResolucaoGuardada()
    {
        var resolucao = ResolucaoEcra.FHD;
        try
        {
            if (File.Exists(CaminhoPreferencia))
            {
                var texto = File.ReadAllText(CaminhoPreferencia).Trim();
                if (Enum.TryParse<ResolucaoEcra>(texto, out var lida)) resolucao = lida;
            }
        }
        catch
        {
            // Se a leitura falhar por qualquer razão, mantém-se o perfil FHD por omissão.
        }

        Aplicar(resolucao, guardarPreferencia: false);
    }

    /// <summary>Aplica o perfil de layout indicado de imediato e (por omissão) grava-o para sessões futuras.</summary>
    public static void Aplicar(ResolucaoEcra resolucao, bool guardarPreferencia = true)
    {
        ResolucaoAtual = resolucao;

        var app = Application.Current;
        var dicionarios = app.Resources.MergedDictionaries;

        // Remove qualquer dicionário de layout (FHD ou UHD) atualmente carregado
        for (var i = dicionarios.Count - 1; i >= 0; i--)
        {
            var origem = dicionarios[i].Source?.OriginalString ?? "";
            if (origem.EndsWith("LayoutFHD.xaml") || origem.EndsWith("LayoutUHD.xaml"))
                dicionarios.RemoveAt(i);
        }

        var novoDicionario = new ResourceDictionary
        {
            Source = new Uri(
                resolucao == ResolucaoEcra.UHD ? "Themes/LayoutUHD.xaml" : "Themes/LayoutFHD.xaml",
                UriKind.Relative)
        };
        dicionarios.Add(novoDicionario);

        ResolucaoMudou?.Invoke(null, resolucao);

        if (guardarPreferencia)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CaminhoPreferencia)!);
                File.WriteAllText(CaminhoPreferencia, resolucao.ToString());
            }
            catch
            {
                // A preferência não é crítica: se não for possível gravar, o perfil
                // continua a ser aplicado normalmente nesta sessão.
            }
        }
    }
}
