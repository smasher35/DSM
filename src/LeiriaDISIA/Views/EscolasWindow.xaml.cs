using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
// A grelha de escolas nesta janela tem x:Name="Grid" (ver Views/EscolasWindow.xaml), o que
// "esconde" o nome do tipo System.Windows.Controls.Grid dentro desta classe — qualquer referência
// nua a "Grid" resolve sempre para esse controlo (o campo gerado pelo x:Name), nunca para o tipo.
// Alias próprio para poder continuar a usar o painel de layout Grid em código (ver
// ConstruirBarraDistribuicao) sem qualificar o nome completo em cada utilização — mesmo problema e
// mesma solução já usados em Views/EquipamentosWindow.xaml.cs.
using WpfGrid = System.Windows.Controls.Grid;

namespace LeiriaDISIA.Views;

public partial class EscolasWindow : Window
{
    private List<Escola> _todas = new();

    /// <summary>Lista atualmente visível na grelha (já com Agrupamento + pesquisa aplicados) —
    /// usada pelo "Relatório do Módulo" para o relatório refletir exatamente o que está a ser
    /// visto (antes só considerava o filtro de Agrupamento, ignorando a pesquisa por texto).</summary>
    private List<Escola> _visiveis = new();

    /// <summary>Capturado uma única vez no construtor (ver Services.JanelaCompactaService) — usado
    /// tanto para a escolha entre os painéis Normal/Compacto de "Infraestrutura e Segurança" como
    /// pelas secções de distribuição dinâmica ("Tipo de Escola", "Estado" — cada uma decide sozinha,
    /// em código, entre gauges e barras finas, ver AtualizarDistribuicaoPorCampo), já que estas são
    /// únicas e não têm uma versão Compacta separada no XAML. Mesmo mecanismo de
    /// Views/EquipamentosWindow.xaml.cs.</summary>
    private bool _modoCompacto;

    public EscolasWindow()
    {
        InitializeComponent();
        // Perfil Guest (Services/SessaoAtual.PodeEditar): acesso só de leitura a este módulo -
        // ver Services/PermissoesService.cs.
        LeiriaDISIA.Services.PermissoesService.AplicarSomenteLeituraSeGuest(BtnInserir);

        // O cabeçalho (cartões-resumo + destaques + gauges) tem um tamanho natural fixo — em ecrãs
        // mais baixos (ex.: portáteis de 13"), reservar-lhe sempre metade da altura disponível (com
        // um mínimo de 280px) garante que sobra sempre espaço a sério para a lista de escolas por
        // baixo, com o cabeçalho a ganhar scroll próprio se não couber tudo. Mesmo mecanismo de
        // Views/EquipamentosWindow.xaml.cs.
        ScrollCabecalho.MaxHeight = Math.Max(280, SystemParameters.WorkArea.Height * 0.5);

        // Modo Compacto (Administração → Aparência): troca os 4 gauges grandes por 4 barras finas
        // no painel "Infraestrutura e Segurança" — só a visibilidade é decidida aqui, uma única vez,
        // na abertura da janela; ambos os painéis são atualizados em AtualizarResumo.
        if (Services.JanelaCompactaService.Ativo)
        {
            _modoCompacto = true;
            PainelInfraestruturaNormal.Visibility = Visibility.Collapsed;
            PainelInfraestruturaCompacto.Visibility = Visibility.Visible;
        }

        CarregarCombos();
        RecarregarGrid();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void CarregarCombos()
    {
        var agrupamentos = App.Db.Agrupamentos.OrderBy(a => a.Nome).ToList();
        var comFiltroTodos = new List<Agrupamento> { new() { Id = 0, Nome = "(Todos os agrupamentos)" } };
        comFiltroTodos.AddRange(agrupamentos);
        CmbFiltroAgrupamento.ItemsSource = comFiltroTodos;
        CmbFiltroAgrupamento.SelectedIndex = 0;
    }

    private void RecarregarGrid()
    {
        _todas = App.Db.Escolas.Include(e => e.Agrupamento).Where(e => e.Estado != EstadosEscola.Desativada).OrderBy(e => e.Nome).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        IEnumerable<Escola> resultado = _todas;

        if (CmbFiltroAgrupamento?.SelectedItem is Agrupamento ag && ag.Id != 0)
            resultado = resultado.Where(e => e.AgrupamentoId == ag.Id);

        var termo = TxtPesquisa?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            resultado = resultado.Where(e =>
                (e.Nome?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Localidade?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Freguesia?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.NomeAlternativo?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Grid.ItemsSource = _visiveis = resultado.ToList();
        AtualizarResumo(_visiveis);
    }

    /// <summary>Preenche todo o painel de resumo (cartões, destaques cruzados e gauges) a partir da
    /// mesma lista já filtrada que alimenta a DataGrid (<see cref="_visiveis"/>) — respeita
    /// automaticamente o agrupamento/pesquisa ativos, sem qualquer consulta adicional para os
    /// cartões e gauges simples (só <see cref="AtualizarDestaques"/> consulta a base de dados
    /// diretamente, para cruzar com Equipamentos/Pedidos/Intervenções). Mesmo padrão visual e mesmo
    /// gauge circular já usados no módulo Equipamentos — ver <see cref="DashboardView.ConstruirGaugePercentagem"/>.</summary>
    private void AtualizarResumo(List<Escola> visiveis)
    {
        TxtTotalEscolas.Text = visiveis.Count.ToString();
        TxtTotalAlunos.Text = visiveis.Sum(e => e.NumeroAlunos ?? 0).ToString();
        TxtTotalAgrupamentos.Text = visiveis.Where(e => e.AgrupamentoId != null).Select(e => e.AgrupamentoId).Distinct().Count().ToString();
        TxtTotalEmObras.Text = visiveis.Count(e => e.Estado == EstadosEscola.EmObras).ToString();
        TxtTotalPorGeocodificar.Text = visiveis.Count(e => e.Latitude == null || e.Longitude == null).ToString();

        AtualizarDestaques(visiveis);

        // ---- Gauges "Infraestrutura e Segurança" (4 níveis fixos) ----
        var total = visiveis.Count;
        TxtTotalInfraestrutura.Text = total == 0
            ? "% de escolas com cada característica (sem escolas a apresentar)"
            : $"% de escolas com cada característica ({total} escolas)";

        void AtualizarGaugeInfraestrutura(LiveChartsCore.SkiaSharpView.WPF.PieChart gauge, TextBlock txt, int comCaracteristica, string corHex)
        {
            gauge.Series = DashboardView.ConstruirGaugePercentagem(comCaracteristica, total, corHex);
            txt.Text = $"{comCaracteristica} / {total}";
        }

        AtualizarGaugeInfraestrutura(GaugeFibra, TxtGaugeFibra, visiveis.Count(e => e.TemInternetFibra), "#2AB7CA");
        AtualizarGaugeInfraestrutura(GaugeCCTV, TxtGaugeCCTV, visiveis.Count(e => e.TemCCTV), "#8B5CF6");
        AtualizarGaugeInfraestrutura(GaugeVPN, TxtGaugeVPN, visiveis.Count(e => e.TemVPN), "#F97316");
        AtualizarGaugeInfraestrutura(GaugeBiblioteca, TxtGaugeBiblioteca, visiveis.Count(e => e.TemBiblioteca), "#22C55E");

        // Versão compacta (Modo Compacto) do mesmo painel — 4 barras finas em vez dos 4 gauges
        // acima. As colunas "Cheia"/"Vazia" de cada barra usam larguras em "*" (estrela), que se
        // ajustam automaticamente à largura real do ecrã, em vez de pixels fixos. Sempre atualizadas
        // (mesmo quando não visíveis) — mais simples do que só calcular quando _modoCompacto está
        // ativo, e o custo é desprezável.
        AtualizarBarraCompacta(ColBarraFibraCheia, ColBarraFibraVazia, TxtBarraFibra, visiveis.Count(e => e.TemInternetFibra), total);
        AtualizarBarraCompacta(ColBarraCCTVCheia, ColBarraCCTVVazia, TxtBarraCCTV, visiveis.Count(e => e.TemCCTV), total);
        AtualizarBarraCompacta(ColBarraVPNCheia, ColBarraVPNVazia, TxtBarraVPN, visiveis.Count(e => e.TemVPN), total);
        AtualizarBarraCompacta(ColBarraBibliotecaCheia, ColBarraBibliotecaVazia, TxtBarraBiblioteca, visiveis.Count(e => e.TemBiblioteca), total);

        // ---- Gauges dinâmicos (nº de valores variável, geridos em Dados Fixos) ----
        AtualizarDistribuicaoPorCampo(visiveis, e => e.Tipo, PainelGaugesTipo, TxtTotalTipo, TxtSemTipo, "cada tipo de escola");
        AtualizarDistribuicaoPorCampo(visiveis, e => e.Estado, PainelGaugesEstado, TxtTotalEstado, TxtSemEstado, "cada estado");
    }

    /// <summary>Cores usadas, por ordem, para os gauges de distribuição dinâmica (Tipo, Estado) —
    /// como o nº de valores distintos varia consoante o que está configurado em Dados Fixos, não há
    /// uma cor "própria" de cada valor: usa-se sempre a próxima cor desta paleta, pela ordem em que
    /// aparecem (do mais para o menos comum), com "Outros" sempre em cinzento. Mesma paleta já usada
    /// para o mesmo efeito em Views/EquipamentosWindow.xaml.cs.</summary>
    private static readonly string[] PaletaDistribuicaoDinamica =
    {
        "#1D4ED8", "#D97706", "#15803D", "#B91C1C", "#7E22CE", "#0F766E", "#BE185D", "#0369A1"
    };

    /// <summary>Agrupa as escolas visíveis pelo valor devolvido por <paramref name="obterValor"/>
    /// (Tipo ou Estado) e desenha um gauge por cada valor distinto encontrado, com a % sobre o
    /// total de escolas COM esse campo preenchido. Só os 6 valores mais comuns aparecem
    /// individualmente — o resto (se houver) é somado num único "Outros", para não sobrecarregar o
    /// painel com valores residuais. Mesmo mecanismo (adaptado para Escola) já usado em "Sistemas
    /// Operativos"/"Armazenamento" no módulo Equipamentos — ver
    /// EquipamentosWindow.xaml.cs → AtualizarDistribuicaoPorCampo.</summary>
    private void AtualizarDistribuicaoPorCampo(List<Escola> visiveis, Func<Escola, string?> obterValor,
        WrapPanel painelDestino, TextBlock txtTotal, TextBlock txtSemDados, string descricaoCampo)
    {
        const int maximoIndividual = 6;

        var comValor = visiveis
            .Select(obterValor)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToList();
        var total = comValor.Count;

        var grupos = comValor
            .GroupBy(v => v)
            .Select(g => (Nome: g.Key, Total: g.Count()))
            .OrderByDescending(g => g.Total)
            .ToList();

        if (grupos.Count > maximoIndividual)
        {
            var principais = grupos.Take(maximoIndividual).ToList();
            var restantes = grupos.Skip(maximoIndividual).Sum(g => g.Total);
            principais.Add(("Outros", restantes));
            grupos = principais;
        }

        txtTotal.Text = total == 0
            ? $"% de escolas com {descricaoCampo} (sem escolas a apresentar)"
            : $"% de escolas com {descricaoCampo} ({total} escolas)";

        txtSemDados.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;

        painelDestino.Children.Clear();

        for (var i = 0; i < grupos.Count; i++)
        {
            var (nome, parcela) = grupos[i];
            var cor = nome == "Outros" ? "#9CA3AF" : PaletaDistribuicaoDinamica[i % PaletaDistribuicaoDinamica.Length];

            if (_modoCompacto)
            {
                painelDestino.Children.Add(ConstruirBarraDistribuicao(nome, parcela, total, cor));
                continue;
            }

            var painel = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 10, 10) };
            painel.Children.Add(new TextBlock
            {
                Text = nome, Style = (Style)FindResource("KpiLabelStyle"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 110, TextAlignment = TextAlignment.Center
            });
            var gauge = new LiveChartsCore.SkiaSharpView.WPF.PieChart
            {
                Height = 110, Width = 110, InitialRotation = -225, MaxAngle = 270, MinValue = 0, MaxValue = 100,
                Series = DashboardView.ConstruirGaugePercentagem(parcela, total, cor)
            };
            painel.Children.Add(gauge);
            painel.Children.Add(new TextBlock
            {
                Text = $"{parcela} / {total}", FontSize = 10, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("BrushTextSecondary")
            });
            painelDestino.Children.Add(painel);
        }
    }

    /// <summary>Constrói uma linha "nome + barra fina + x/total" para o Modo Compacto das secções de
    /// distribuição dinâmica ("Tipo de Escola", "Estado") — mesmo estilo visual das barras finas do
    /// painel compacto de "Infraestrutura e Segurança" (ver PainelInfraestruturaCompacto no XAML),
    /// mas montada em código porque o número de valores distintos é dinâmico (ali são sempre 4
    /// barras fixas, declaradas no XAML). Réplica exata (adaptada ao alias WpfGrid) do método
    /// homónimo em Views/EquipamentosWindow.xaml.cs.</summary>
    private static WpfGrid ConstruirBarraDistribuicao(string nome, int parcela, int total, string corHex)
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

    /// <summary>Ajusta a largura preenchida de uma barra fina do painel compacto de "Infraestrutura
    /// e Segurança" (ver <see cref="AtualizarResumo"/>) para refletir a percentagem de
    /// <paramref name="parcela"/> sobre <paramref name="total"/>, e atualiza o texto "x / total" ao
    /// lado. Réplica exata do método homónimo em Views/EquipamentosWindow.xaml.cs.</summary>
    private static void AtualizarBarraCompacta(ColumnDefinition colCheia, ColumnDefinition colVazia, TextBlock txt, int parcela, int total)
    {
        var percentagem = total == 0 ? 0 : (parcela * 100.0 / total);
        colCheia.Width = new GridLength(percentagem, GridUnitType.Star);
        colVazia.Width = new GridLength(100 - percentagem, GridUnitType.Star);
        txt.Text = $"{parcela} / {total}";
    }

    /// <summary>Destaques cruzados com outros módulos: escola com mais equipamento instalado, mais
    /// pedidos de intervenção em aberto e mais intervenções registadas (histórico completo, sem
    /// restrição de data — mesmo critério "sempre" já usado no destaque equivalente de
    /// Equipamentos). Cada painel só fica visível quando há pelo menos uma escola com valor > 0 a
    /// apresentar, para não mostrar uma frase vazia numa base de dados nova ou já toda filtrada.
    /// Os três totais são pré-calculados de uma só vez por tabela (GroupBy), em vez de uma consulta
    /// por escola, tal como em EquipamentosWindow.xaml.cs → AtualizarMaisIntervencionado.</summary>
    private void AtualizarDestaques(List<Escola> visiveis)
    {
        if (visiveis.Count == 0)
        {
            PainelEscolaMaisEquipamento.Visibility = Visibility.Collapsed;
            PainelEscolaMaisPedidos.Visibility = Visibility.Collapsed;
            PainelEscolaMaisIntervencoes.Visibility = Visibility.Collapsed;
            return;
        }

        var idsVisiveis = visiveis.Select(e => e.Id).ToHashSet();

        var porEquipamento = App.Db.Equipamentos
            .Where(eq => eq.EscolaId != null && idsVisiveis.Contains(eq.EscolaId.Value))
            .GroupBy(eq => eq.EscolaId!.Value)
            .Select(g => new { EscolaId = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();
        DefinirDestaque(PainelEscolaMaisEquipamento, TxtEscolaMaisEquipamento, visiveis, porEquipamento?.EscolaId, porEquipamento?.Total, "equipamento(s)");

        var porPedidosAbertos = App.Db.PedidosIntervencao
            .Where(p => idsVisiveis.Contains(p.EscolaId) &&
                        (p.Estado == EstadoPedido.Pendente || p.Estado == EstadoPedido.EmAndamento || p.Estado == EstadoPedido.EmEspera))
            .GroupBy(p => p.EscolaId)
            .Select(g => new { EscolaId = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();
        DefinirDestaque(PainelEscolaMaisPedidos, TxtEscolaMaisPedidos, visiveis, porPedidosAbertos?.EscolaId, porPedidosAbertos?.Total, "pedido(s) em aberto");

        var porIntervencoes = App.Db.Intervencoes
            .Where(i => idsVisiveis.Contains(i.EscolaId))
            .GroupBy(i => i.EscolaId)
            .Select(g => new { EscolaId = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();
        DefinirDestaque(PainelEscolaMaisIntervencoes, TxtEscolaMaisIntervencoes, visiveis, porIntervencoes?.EscolaId, porIntervencoes?.Total, "intervenção(ões)");
    }

    /// <summary>Aplica o resultado de uma das três consultas de <see cref="AtualizarDestaques"/> ao
    /// painel/texto correspondente — esconde o painel quando não há nenhuma escola com valor > 0
    /// (ex.: nenhum pedido em aberto entre as escolas visíveis). Recebe um <see cref="Run"/>, não um
    /// <see cref="TextBlock"/> — no XAML (ver Views/EscolasWindow.xaml) cada texto de destaque é um
    /// "&lt;Run x:Name=...&gt;" dentro de um TextBlock com TextWrapping="Wrap" (mesmo padrão já usado
    /// nos painéis "Mais Intervencionado" de Equipamentos), precisamente para poder quebrar linha; um
    /// Run tem a sua própria propriedade Text, mas não é um TextBlock.</summary>
    private static void DefinirDestaque(Border painel, Run txt, List<Escola> visiveis, int? escolaId, int? total, string unidade)
    {
        if (escolaId == null || total is null or 0)
        {
            painel.Visibility = Visibility.Collapsed;
            return;
        }

        var escola = visiveis.First(e => e.Id == escolaId.Value);
        txt.Text = $"{escola.Nome} — {total} {unidade}";
        painel.Visibility = Visibility.Visible;
    }

    private void Filtro_Changed(object sender, SelectionChangedEventArgs e) => AplicarFiltro();
    private void Filtro_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new EscolaEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) RecarregarGrid();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not Escola escola) return;

        var janela = new EscolaEditWindow(escola) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) RecarregarGrid();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        // (1.1/4.1) O relatório deve refletir apenas o que está a ser visualizado: mantém o título
        // e o nome de ficheiro a refletirem o agrupamento selecionado (quando aplicável), mas passa
        // também a lista completa de IDs atualmente visíveis, para a pesquisa por texto (que antes
        // era ignorada aqui) ser igualmente respeitada, e para poder bloquear a geração se o filtro
        // não corresponder a nenhuma escola.
        if (_visiveis.Count == 0)
        {
            MessageBox.Show("Não existem escolas a corresponder ao filtro atual para incluir no relatório.",
                "Sem dados para o relatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var agrupamentoSelecionado = CmbFiltroAgrupamento?.SelectedItem as Agrupamento;
        var filtrado = agrupamentoSelecionado is not null && agrupamentoSelecionado.Id != 0;

        var nomeFicheiroBase = filtrado
            ? $"Lista_Escolas_{LimparNomeFicheiro(agrupamentoSelecionado!.Nome)}"
            : "Lista_Total_Escolas";

        var dialog = new SaveFileDialog
        {
            Title = filtrado ? "Guardar lista de escolas do agrupamento" : "Guardar lista total de escolas",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"{nomeFicheiroBase}_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaTotalEscolas(dialog.FileName, filtrado ? agrupamentoSelecionado!.Id : null,
                _visiveis.Select(esc => esc.Id).ToList());

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

    private static string LimparNomeFicheiro(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "Agrupamento";
        var invalidos = Path.GetInvalidFileNameChars();
        var limpo = new string(nome.Where(c => !invalidos.Contains(c)).ToArray());
        return limpo.Replace(" ", "_");
    }
}
