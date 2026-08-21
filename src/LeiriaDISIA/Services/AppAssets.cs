using System.IO;
using System.Windows;

namespace LeiriaDISIA.Services;

/// <summary>
/// Acesso aos recursos gráficos incorporados na aplicação (ex.: o logótipo da DISIA), para uso
/// tanto na interface (XAML normalmente usa o pack URI diretamente) como em código, nomeadamente
/// nos relatórios PDF gerados pelo QuestPDF, que precisam dos bytes da imagem em memória.
/// </summary>
public static class AppAssets
{
    private static byte[]? _logoDisia;
    private static byte[]? _logoMunicipio;

    /// <summary>Bytes PNG do logótipo oficial da DISIA (Município de Leiria). Lidos uma única vez
    /// a partir do recurso incorporado da aplicação e mantidos em cache em memória.</summary>
    public static byte[] LogoDisia => _logoDisia ??= CarregarRecurso("Assets/disia_logo.png");

    /// <summary>Bytes PNG do brasão/logótipo do Município de Leiria, usado na capa do Relatório
    /// Mensal de Atividades em PDF.</summary>
    public static byte[] LogoMunicipio => _logoMunicipio ??= CarregarRecurso("Assets/logo_municipio.png");

    private static byte[] CarregarRecurso(string caminhoRelativo)
    {
        var uri = new Uri($"pack://application:,,,/{caminhoRelativo}", UriKind.Absolute);
        var info = Application.GetResourceStream(uri)
            ?? throw new InvalidOperationException($"Recurso incorporado não encontrado: {caminhoRelativo}");

        using var stream = info.Stream;
        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }
}
