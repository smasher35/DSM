using System.IO;
using System.Windows;

namespace LeiriaDISIA.Services;

public enum TemaAplicacao
{
    Claro,
    Escuro
}

/// <summary>
/// Permite alternar entre o tema Claro e Escuro em tempo de execução, e recorda a
/// preferência do utilizador entre sessões (ficheiro de texto simples, sem necessidade
/// de alterar o esquema da base de dados).
/// </summary>
public static class ThemeService
{
    private static readonly string CaminhoPreferencia = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LeiriaDISIA", "tema.txt");

    public static TemaAplicacao TemaAtual { get; private set; } = TemaAplicacao.Claro;

    /// <summary>Evento disparado quando o tema muda</summary>
    public static event EventHandler<TemaAplicacao>? TemaMudou;

    /// <summary>Lê a preferência gravada (se existir) e aplica-a. A chamar uma vez, no arranque da aplicação.</summary>
    public static void AplicarTemaGuardado()
    {
        var tema = TemaAplicacao.Claro;
        try
        {
            if (File.Exists(CaminhoPreferencia))
            {
                var texto = File.ReadAllText(CaminhoPreferencia).Trim();
                if (Enum.TryParse<TemaAplicacao>(texto, out var lido)) tema = lido;
            }
        }
        catch
        {
            // Se a leitura falhar por qualquer razão, mantém-se o tema claro por omissão.
        }

        Aplicar(tema, guardarPreferencia: false);
    }

    /// <summary>Aplica o tema indicado de imediato e (por omissão) grava-o para sessões futuras.</summary>
    public static void Aplicar(TemaAplicacao tema, bool guardarPreferencia = true)
    {
        TemaAtual = tema;

        var app = Application.Current;
        var dicionarios = app.Resources.MergedDictionaries;

        // Remove qualquer dicionário de tema (Claro ou Escuro) atualmente carregado
        for (var i = dicionarios.Count - 1; i >= 0; i--)
        {
            var origem = dicionarios[i].Source?.OriginalString ?? "";
            if (origem.EndsWith("ModernTheme.xaml") || origem.EndsWith("DarkTheme.xaml"))
                dicionarios.RemoveAt(i);
        }

        var novoDicionario = new ResourceDictionary
        {
            Source = new Uri(
                tema == TemaAplicacao.Escuro ? "Themes/DarkTheme.xaml" : "Themes/ModernTheme.xaml",
                UriKind.Relative)
        };
        dicionarios.Add(novoDicionario);

        // Ajusta também o tema nativo do ModernWpf (afeta controlos como ComboBox, DatePicker, etc.)
        ModernWpf.ThemeManager.Current.ApplicationTheme =
            tema == TemaAplicacao.Escuro ? ModernWpf.ApplicationTheme.Dark : ModernWpf.ApplicationTheme.Light;

        // Dispara o evento para notificar que o tema mudou
        TemaMudou?.Invoke(null, tema);

        if (guardarPreferencia)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CaminhoPreferencia)!);
                File.WriteAllText(CaminhoPreferencia, tema.ToString());
            }
            catch
            {
                // A preferência não é crítica: se não for possível gravar, o tema
                // continua a ser aplicado normalmente nesta sessão.
            }
        }
    }
}
