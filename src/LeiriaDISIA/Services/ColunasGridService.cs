using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LeiriaDISIA.Services;

/// <summary>
/// Guarda e repõe, entre sessões, a largura das colunas de todos os DataGrids da aplicação.
///
/// Ativado globalmente através de um Setter no Style de DataGrid dos temas (ModernTheme.xaml /
/// DarkTheme.xaml), pelo que nenhuma janela precisa de código extra para beneficiar disto.
///
/// Comportamento:
///  - Ao abrir uma janela, se existir uma largura gravada para uma coluna, essa largura é
///    aplicada; caso contrário, a coluna mantém o ajuste automático ao conteúdo (Width="Auto"
///    definido nas Views).
///  - A largura só é gravada quando o utilizador larga o rato depois de arrastar a fronteira de
///    uma coluna (evento DragCompleted dos "grips" de cabeçalho) — nunca a partir do simples
///    ajuste automático ao conteúdo, para que colunas ainda não redimensionadas manualmente
///    continuem sempre a acompanhar o comprimento dos dados mais recentes.
/// </summary>
public static class ColunasGridService
{
    private static readonly string CaminhoFicheiro = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LeiriaDISIA", "larguras-colunas.json");

    private static Dictionary<string, Dictionary<string, double>>? _cache;

    public static readonly DependencyProperty PersistirLargurasProperty =
        DependencyProperty.RegisterAttached(
            "PersistirLarguras", typeof(bool), typeof(ColunasGridService),
            new PropertyMetadata(false, OnPersistirLargurasChanged));

    public static void SetPersistirLarguras(DependencyObject obj, bool value) => obj.SetValue(PersistirLargurasProperty, value);
    public static bool GetPersistirLarguras(DependencyObject obj) => (bool)obj.GetValue(PersistirLargurasProperty);

    private static void OnPersistirLargurasChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || e.NewValue is not true) return;

        // Evita subscrições duplicadas caso o Style seja reaplicado à mesma grelha (ex.: troca
        // de tema Claro/Escuro em tempo real com a janela já aberta).
        grid.Loaded -= Grid_Loaded;
        grid.Loaded += Grid_Loaded;
        grid.RemoveHandler(Thumb.DragCompletedEvent, (DragCompletedEventHandler)Grid_ColunaRedimensionada);
        // O evento de "arrastar terminado" nasce no Thumb do cabeçalho da coluna e propaga-se
        // (bubbling) até ao próprio DataGrid — mas o próprio DataGridColumnHeader do WPF já o
        // marca como "Handled" ao usá-lo para redimensionar a coluna, por isso é preciso o
        // "handledEventsToo: true" para ainda assim conseguirmos ouvi-lo aqui.
        grid.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(Grid_ColunaRedimensionada), true);
    }

    private static void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid) AplicarLargurasGuardadas(grid);
    }

    private static void Grid_ColunaRedimensionada(object sender, DragCompletedEventArgs e)
    {
        // O DragCompleted também dispara ao arrastar outros Thumbs eventualmente presentes na
        // árvore visual (ex.: scrollbars); só nos interessa quando quem foi arrastado é
        // efetivamente um dos "grips" de fronteira de coluna definidos nos temas.
        if (e.OriginalSource is not Thumb thumb) return;
        if (thumb.Name != "PART_LeftHeaderGripper" && thumb.Name != "PART_RightHeaderGripper") return;
        if (sender is DataGrid grid) GuardarLargurasAtuais(grid);
    }

    private static void AplicarLargurasGuardadas(DataGrid grid)
    {
        try
        {
            var cache = ObterCache();
            if (!cache.TryGetValue(ObterChaveGrid(grid), out var larguras)) return;

            for (var i = 0; i < grid.Columns.Count; i++)
            {
                var coluna = grid.Columns[i];
                if (larguras.TryGetValue(ObterChaveColuna(coluna, i), out var largura) && largura > 0)
                    coluna.Width = new DataGridLength(largura);
            }
        }
        catch
        {
            // As larguras gravadas não são críticas: se a leitura falhar por qualquer razão, os
            // DataGrids continuam a funcionar normalmente com o ajuste automático ao conteúdo.
        }
    }

    private static void GuardarLargurasAtuais(DataGrid grid)
    {
        try
        {
            var cache = ObterCache();
            var larguras = new Dictionary<string, double>();

            for (var i = 0; i < grid.Columns.Count; i++)
            {
                var coluna = grid.Columns[i];
                if (double.IsNaN(coluna.ActualWidth) || coluna.ActualWidth <= 0) continue;
                larguras[ObterChaveColuna(coluna, i)] = coluna.ActualWidth;
            }

            cache[ObterChaveGrid(grid)] = larguras;

            Directory.CreateDirectory(Path.GetDirectoryName(CaminhoFicheiro)!);
            File.WriteAllText(CaminhoFicheiro, JsonSerializer.Serialize(cache));
        }
        catch
        {
            // Gravar as larguras é um "extra" de conforto; nunca deve impedir o funcionamento
            // normal da aplicação nem interromper o que o utilizador estava a fazer.
        }
    }

    private static Dictionary<string, Dictionary<string, double>> ObterCache()
    {
        if (_cache != null) return _cache;

        try
        {
            if (File.Exists(CaminhoFicheiro))
            {
                var json = File.ReadAllText(CaminhoFicheiro);
                _cache = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(json)
                         ?? new Dictionary<string, Dictionary<string, double>>();
                return _cache;
            }
        }
        catch
        {
            // Ficheiro inexistente, corrompido ou ilegível — começa com uma "memória" vazia em
            // vez de impedir o arranque da aplicação.
        }

        _cache = new Dictionary<string, Dictionary<string, double>>();
        return _cache;
    }

    /// <summary>Identifica de forma estável cada DataGrid entre sessões, sem precisar de
    /// nenhuma configuração por janela: usa o tipo da Window ou UserControl que o contém, mais
    /// o seu x:Name (cada DataGrid da aplicação tem um x:Name único dentro do seu ecrã).</summary>
    private static string ObterChaveGrid(DataGrid grid)
    {
        DependencyObject? atual = grid;
        while (atual != null)
        {
            if (atual is Window || atual is UserControl)
            {
                var nomeTipo = atual.GetType().FullName ?? atual.GetType().Name;
                return $"{nomeTipo}.{grid.Name}";
            }
            atual = LogicalTreeHelper.GetParent(atual) ?? VisualTreeHelper.GetParent(atual);
        }

        return $"{grid.GetType().Name}.{grid.Name}";
    }

    /// <summary>Identifica cada coluna pelo texto do seu cabeçalho — estável mesmo que o
    /// utilizador reordene colunas (CanUserReorderColumns). Colunas sem cabeçalho de texto
    /// (ex.: botões de ação) usam a posição como identificador de reserva.</summary>
    private static string ObterChaveColuna(DataGridColumn coluna, int indice) =>
        coluna.Header?.ToString() is { Length: > 0 } texto ? texto : $"#{indice}";
}
