using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
// A grelha de intervenções nesta janela tem x:Name="Grid" (ver Views/IntervencoesWindow.xaml), o
// que "esconde" o nome do tipo System.Windows.Controls.Grid dentro desta classe — qualquer
// referência nua a "Grid" resolve sempre para esse controlo (o campo gerado pelo x:Name), nunca
// para o tipo, mesmo em métodos estáticos (mesmo problema, e mesma solução, já usados em
// Views/EquipamentosWindow.xaml.cs). Alias próprio para poder continuar a usar o painel de layout
// Grid em código (ver ConstruirBarraCategoria) sem qualificar o nome completo em cada utilização.
using WpfGrid = System.Windows.Controls.Grid;

namespace LeiriaDISIA.Views;

public partial class IntervencoesWindow : Window
{
    private static readonly string[] NomesMeses =
    {
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    };

    /// <summary>Lista atualmente visível na grelha (já com Ano/Mês/Agrupamento/pesquisa
    /// aplicados) — usada pelo "Relatório do Módulo" para o relatório refletir exatamente o que
    /// está a ser visto.</summary>
    private List<Intervencao> _visiveis = new();

    /// <summary>Capturado uma única vez no construtor (ver Services.JanelaCompactaService) — usado
    /// pelas secções de distribuição por categoria (Total Anual/Mês Corrente), que decidem
    /// sozinhas, em código, entre gauges e barras finas — ver AtualizarGaugesCategoria — já que
    /// não têm uma versão Compacta separada no XAML (mesmo padrão de
    /// Views/EquipamentosWindow.xaml.cs).</summary>
    private bool _modoCompacto;

    public IntervencoesWindow()
    {
        InitializeComponent();
        // Perfil Guest (Services/SessaoAtual.PodeEditar): acesso só de leitura a este módulo -
        // ver Services/PermissoesService.cs.
        LeiriaDISIA.Services.PermissoesService.AplicarSomenteLeituraSeGuest(BtnInserir);

        _modoCompacto = LeiriaDISIA.Services.JanelaCompactaService.Ativo;

        var anoAtual = DateTime.Today.Year;
        CmbAno.ItemsSource = Enumerable.Range(anoAtual - 3, 6).ToList();
        CmbAno.SelectedItem = anoAtual;

        var meses = new List<string> { "(Todos)" };
        meses.AddRange(NomesMeses);
        CmbMes.ItemsSource = meses;
        CmbMes.SelectedIndex = DateTime.Today.Month;

        var agrupamentos = new List<Agrupamento> { new() { Id = 0, Nome = "(Todos)" } };
        agrupamentos.AddRange(App.Db.Agrupamentos.OrderBy(a => a.Nome));
        CmbAgrupamentoFiltro.ItemsSource = agrupamentos;
        CmbAgrupamentoFiltro.SelectedIndex = 0;

        // (4.1) Legenda dos quadrados de cor da coluna "Categorias"
        LegendaCategorias.ItemsSource = App.Db.CategoriasIntervencao.OrderBy(c => c.Nome).ToList();

        Recarregar();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Filtro_Changed(object sender, SelectionChangedEventArgs e) => Recarregar();
    private void Filtro_TextChanged(object sender, TextChangedEventArgs e) => Recarregar();

    private void Recarregar()
    {
        if (CmbAno == null || Grid == null) return;

        var ano = (int?)CmbAno.SelectedItem ?? DateTime.Today.Year;
        var mesIndex = CmbMes.SelectedIndex;
        var agrupamentoSel = CmbAgrupamentoFiltro.SelectedItem as Agrupamento;

        var query = App.Db.Intervencoes
            .Include(i => i.Escola)
            .Include(i => i.Agrupamento)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .Where(i => i.Ano == ano)
            .AsQueryable();

        if (mesIndex > 0) query = query.Where(i => i.Mes == mesIndex);
        if (agrupamentoSel != null && agrupamentoSel.Id != 0) query = query.Where(i => i.AgrupamentoId == agrupamentoSel.Id);

        // A partir daqui a pesquisa por texto livre é feita em memória (LINQ-to-Objects), não na
        // base de dados: o SQLite/EF Core não consegue traduzir para SQL a sobrecarga
        // "string.Contains(texto, StringComparison)" usada abaixo (dava erro "could not be
        // translated" ao pesquisar) — juntar ".ToList()" aqui, logo a seguir aos filtros que a
        // base de dados já sabe traduzir (Ano/Mês/Agrupamento), resolve isso sem perder a pesquisa
        // insensível a maiúsculas/minúsculas.
        IEnumerable<Intervencao> resultado = query.ToList();

        // Aplicar pesquisa
        var termo = TxtPesquisa?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            resultado = resultado.Where(i =>
                (i.Escola != null && i.Escola.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (i.Descricao != null && i.Descricao.Contains(termo, StringComparison.OrdinalIgnoreCase)));
        }

        Grid.ItemsSource = _visiveis = resultado.OrderByDescending(i => i.Data).ToList();

        AtualizarResumoIntervencoes(ano);
    }

    /// <summary>Atualiza os três cartões "mais intervencionada do ano" (Categoria/Agrupamento/
    /// Escola) e as duas secções de gauges "por Categoria" (Total Anual/Mês Corrente). Chamado a
    /// cada Recarregar(), para se manter atualizado sempre que uma intervenção é inserida/editada,
    /// mesmo que a alteração não afete os filtros atualmente aplicados na grelha.
    ///
    /// Os três cartões e o gauge "Total Anual" usam sempre o Ano escolhido no filtro acima (não o
    /// Mês/Agrupamento/pesquisa também aplicados à grelha — mostram sempre o ano inteiro, para se
    /// ver o panorama anual independentemente do que estiver filtrado por baixo). O gauge "Mês
    /// Corrente" usa sempre o mês atual (DateTime.Today), não o Mês escolhido no filtro, tal como
    /// os cartões equivalentes do Dashboard — ver Services/DashboardService.cs.</summary>
    private void AtualizarResumoIntervencoes(int ano)
    {
        var hoje = DateTime.Today;

        var intervencoesDoAno = App.Db.Intervencoes
            .Include(i => i.Escola)
            .Include(i => i.Agrupamento)
            .Where(i => i.Estado != EstadoIntervencao.Cancelada && i.Data.Year == ano)
            .ToList();

        // Categoria mais intervencionada do ano — uma intervenção pode ter mais do que uma
        // categoria (ver Intervencao.Categorias), por isso conta-se pela tabela de junção
        // IntervencaoCategorias, não pela lista de intervenções acima.
        var categoriaMaisIntervencionada = App.Db.IntervencaoCategorias
            .Include(ic => ic.Categoria)
            .Where(ic => ic.Intervencao!.Estado != EstadoIntervencao.Cancelada && ic.Intervencao.Data.Year == ano)
            .GroupBy(ic => ic.CategoriaIntervencaoId)
            .Select(g => new { Categoria = g.First().Categoria!, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();

        if (categoriaMaisIntervencionada != null)
        {
            var vezes = categoriaMaisIntervencionada.Total == 1 ? "1 intervenção" : $"{categoriaMaisIntervencionada.Total} intervenções";
            TxtCategoriaMaisIntervencionada.Text = $"{categoriaMaisIntervencionada.Categoria.Nome} — {vezes}";
            PainelCategoriaMaisIntervencionada.Visibility = Visibility.Visible;
        }
        else
        {
            PainelCategoriaMaisIntervencionada.Visibility = Visibility.Collapsed;
        }

        // Agrupamento mais intervencionado do ano
        var agrupamentoMaisIntervencionado = intervencoesDoAno
            .Where(i => i.AgrupamentoId != null)
            .GroupBy(i => i.AgrupamentoId!.Value)
            .Select(g => new { Agrupamento = g.First().Agrupamento!, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();

        if (agrupamentoMaisIntervencionado != null)
        {
            var vezes = agrupamentoMaisIntervencionado.Total == 1 ? "1 intervenção" : $"{agrupamentoMaisIntervencionado.Total} intervenções";
            TxtAgrupamentoMaisIntervencionadoAno.Text = $"{agrupamentoMaisIntervencionado.Agrupamento.Nome} — {vezes}";
            PainelAgrupamentoMaisIntervencionadoAno.Visibility = Visibility.Visible;
        }
        else
        {
            PainelAgrupamentoMaisIntervencionadoAno.Visibility = Visibility.Collapsed;
        }

        // Escola mais intervencionada do ano
        var escolaMaisIntervencionada = intervencoesDoAno
            .GroupBy(i => i.EscolaId)
            .Select(g => new { Escola = g.First().Escola!, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();

        if (escolaMaisIntervencionada != null)
        {
            var vezes = escolaMaisIntervencionada.Total == 1 ? "1 intervenção" : $"{escolaMaisIntervencionada.Total} intervenções";
            TxtEscolaMaisIntervencionadaAno.Text = $"{escolaMaisIntervencionada.Escola.Nome} — {vezes}";
            PainelEscolaMaisIntervencionadaAno.Visibility = Visibility.Visible;
        }
        else
        {
            PainelEscolaMaisIntervencionadaAno.Visibility = Visibility.Collapsed;
        }

        // Gauges "por Categoria": Total Anual (mesmo Ano do filtro) e Mês Corrente (sempre
        // DateTime.Today, independentemente do Ano/Mês escolhidos no filtro da grelha).
        AtualizarGaugesCategoria(ano, mes: null, PainelGaugesCategoriaAno, TxtTotalCategoriaAno, TxtSemCategoriaAno, "do ano");
        AtualizarGaugesCategoria(hoje.Year, hoje.Month, PainelGaugesCategoriaMes, TxtTotalCategoriaMes, TxtSemCategoriaMes, "do mês corrente");
    }

    /// <summary>Agrupa as intervenções (não canceladas) de <paramref name="ano"/> — ou desse ano e
    /// <paramref name="mes"/> em concreto, quando indicado — pelas suas categorias (uma
    /// intervenção pode ter mais do que uma) e desenha um gauge — ou, em Modo Compacto, uma barra
    /// fina — por cada categoria encontrada, com a % sobre o total de categorias-em-intervenções
    /// do período (não sobre o nº de intervenções: uma intervenção com 2 categorias conta 2 vezes,
    /// uma por cada categoria). Cada gauge usa a mesma cor (CategoriaIntervencao.CorHex) já
    /// configurada para essa categoria nos badges da grelha e na legenda "Categorias:" no topo da
    /// janela, para se conseguir associar visualmente um gauge à sua categoria de imediato — em
    /// vez de uma paleta de cores arbitrária, sem correspondência com o resto da janela. Para não
    /// sobrecarregar o painel com categorias residuais, só as 6 mais comuns aparecem
    /// individualmente — o resto (se houver) é somado num único "Outras", sempre a cinzento (não
    /// faria sentido usar a cor de nenhuma categoria em particular para representar várias juntas).
    /// Mesmo mecanismo de AtualizarDistribuicaoPorCampo em Views/EquipamentosWindow.xaml.cs,
    /// adaptado para uma relação muitos-para-muitos (categorias) em vez de um campo simples.</summary>
    private void AtualizarGaugesCategoria(int ano, int? mes, WrapPanel painelDestino, TextBlock txtTotal, TextBlock txtSemDados, string descricaoPeriodo)
    {
        const int maximoIndividual = 6;

        var query = App.Db.IntervencaoCategorias
            .Include(ic => ic.Categoria)
            .Where(ic => ic.Intervencao!.Estado != EstadoIntervencao.Cancelada && ic.Intervencao.Data.Year == ano);
        if (mes != null) query = query.Where(ic => ic.Intervencao!.Data.Month == mes);

        // Extrai Nome + CorHex (a mesma cor já configurada para os badges desta categoria na
        // grelha e na legenda "Categorias:" no topo da janela — ver
        // CategoriaIntervencao.CorHex) em vez de só o nome, para os gauges usarem sempre a mesma
        // cor de cada categoria em todo o lado, tornando mais fácil associar visualmente um gauge
        // à sua categoria na grelha por baixo.
        var categoriasRegistadas = query.Select(ic => new { ic.Categoria!.Nome, ic.Categoria.CorHex }).ToList();
        var total = categoriasRegistadas.Count;

        var grupos = categoriasRegistadas
            .GroupBy(c => c.Nome)
            .Select(g => (Nome: g.Key, CorHex: g.First().CorHex, Total: g.Count()))
            .OrderByDescending(g => g.Total)
            .ToList();

        if (grupos.Count > maximoIndividual)
        {
            var principais = grupos.Take(maximoIndividual).ToList();
            var restantes = grupos.Skip(maximoIndividual).Sum(g => g.Total);
            // "Outras" junta várias categorias diferentes — não tem sentido usar a cor de
            // nenhuma delas em particular, por isso usa sempre cinzento (tal como "Outros" nas
            // secções equivalentes de Views/EquipamentosWindow.xaml.cs).
            principais.Add(("Outras", "#9CA3AF", restantes));
            grupos = principais;
        }

        txtTotal.Text = total == 0
            ? $"% de cada categoria sobre o total {descricaoPeriodo} (sem intervenções a apresentar)"
            : $"% de cada categoria sobre o total {descricaoPeriodo} ({total} categorias registadas)";

        txtSemDados.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;

        painelDestino.Children.Clear();

        for (var i = 0; i < grupos.Count; i++)
        {
            var (nome, cor, parcela) = grupos[i];

            if (_modoCompacto)
            {
                painelDestino.Children.Add(ConstruirBarraCategoria(nome, parcela, total, cor));
            }
            else
            {
                var painel = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 10, 10) };
                painel.Children.Add(new TextBlock
                {
                    // Height fixa (para 2 linhas) + VerticalAlignment=Bottom: "Redes e
                    // Comunicações" ocupa duas linhas, ao contrário de "Hardware"/"Software"/"VPN"
                    // (uma linha só) — sem isto, os gauges ficavam desalinhados entre si, um pouco
                    // mais abaixo nessa coluna do que nas restantes. Alinhar todos os títulos ao
                    // fundo de uma área de altura fixa garante que o gauge a seguir começa sempre
                    // exatamente na mesma posição, tenha o título uma ou duas linhas.
                    Text = nome, Style = (Style)FindResource("KpiLabelStyle"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 130, TextAlignment = TextAlignment.Center,
                    Height = 36, VerticalAlignment = System.Windows.VerticalAlignment.Bottom
                });
                var gauge = new LiveChartsCore.SkiaSharpView.WPF.PieChart
                {
                    Height = 140, Width = 140, InitialRotation = -225, MaxAngle = 270, MinValue = 0, MaxValue = 100,
                    Series = DashboardView.ConstruirGaugePercentagem(parcela, total, cor)
                };
                painel.Children.Add(gauge);
                painel.Children.Add(new TextBlock
                {
                    Text = $"{parcela} / {total}", FontSize = 11, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = (Brush)FindResource("BrushTextSecondary")
                });
                painelDestino.Children.Add(painel);
            }
        }
    }

    /// <summary>Constrói uma linha "nome + barra fina + x/total" para o Modo Compacto das secções
    /// de distribuição por categoria — mesmo estilo visual e mesma construção de
    /// ConstruirBarraDistribuicao em Views/EquipamentosWindow.xaml.cs (duplicado aqui, não
    /// partilhado entre janelas, tal como o resto do código de cada janela desta aplicação).</summary>
    private static WpfGrid ConstruirBarraCategoria(string nome, int parcela, int total, string corHex)
    {
        var linha = new WpfGrid { Width = 260, Margin = new Thickness(0, 0, 0, 6) };
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var txtNome = new TextBlock
        {
            Text = nome, VerticalAlignment = VerticalAlignment.Center, FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        WpfGrid.SetColumn(txtNome, 0);

        var percentagem = total == 0 ? 0 : parcela * 100.0 / total;
        var barraContainer = new WpfGrid { Height = 6, Margin = new Thickness(8, 0, 8, 0) };
        WpfGrid.SetColumn(barraContainer, 1);
        barraContainer.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)), CornerRadius = new CornerRadius(3) });
        var barraInterna = new WpfGrid();
        barraInterna.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(percentagem, GridUnitType.Star) });
        barraInterna.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - percentagem, GridUnitType.Star) });
        var barraCheia = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(corHex)!),
            CornerRadius = new CornerRadius(3)
        };
        WpfGrid.SetColumn(barraCheia, 0);
        barraInterna.Children.Add(barraCheia);
        barraContainer.Children.Add(barraInterna);

        var txtValor = new TextBlock
        {
            Text = $"{parcela} / {total}", FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))
        };
        WpfGrid.SetColumn(txtValor, 2);

        linha.Children.Add(txtNome);
        linha.Children.Add(barraContainer);
        linha.Children.Add(txtValor);
        return linha;
    }

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new IntervencaoEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    /// <summary>Abre o formulário "Inserir Intervenção" já com o diálogo de seleção de PDF
    /// aberto (ver <see cref="IntervencaoEditWindow"/>, construtor com <c>importarPdfAoAbrir: true</c>)
    /// — atalho para quem já sabe que vai importar, sem ter de clicar outra vez no botão "📄
    /// Importar do PDF" que também existe dentro do formulário.</summary>
    private void ImportarPdf_Click(object sender, RoutedEventArgs e)
    {
        var janela = new IntervencaoEditWindow(null, importarPdfAoAbrir: true) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not Intervencao intervencao) return;

        var janela = new IntervencaoEditWindow(intervencao) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        // (1.1) O relatório do módulo reflete exatamente o que está a ser visto na grelha
        // (Ano/Mês/Agrupamento/pesquisa aplicados), em vez de todas as intervenções do ano.
        if (_visiveis.Count == 0)
        {
            MessageBox.Show("Não existem intervenções a corresponder ao filtro atual para incluir no relatório.",
                "Sem dados para o relatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ano = (int?)CmbAno.SelectedItem ?? DateTime.Today.Year;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de intervenções",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Intervencoes_{ano}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaIntervencoes(dialog.FileName, ano, idsFiltrados: _visiveis.Select(i => i.Id).ToList());

            var abrir = MessageBox.Show("Relatório PDF gerado com sucesso. Deseja abri-lo agora?",
                "Concluído", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (abrir == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar o relatório:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
