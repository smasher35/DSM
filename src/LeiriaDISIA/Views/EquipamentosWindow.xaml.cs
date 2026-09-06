using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
// A grelha de equipamento nesta janela tem x:Name="Grid" (ver Views/EquipamentosWindow.xaml), o
// que "esconde" o nome do tipo System.Windows.Controls.Grid dentro desta classe — qualquer
// referência nua a "Grid" resolve sempre para esse controlo (o campo gerado pelo x:Name), nunca
// para o tipo, mesmo em métodos estáticos (CS0120: "An object reference is required..."). Alias
// próprio para poder continuar a usar o painel de layout Grid em código (ver
// ConstruirBarraDistribuicao) sem qualificar o nome completo em cada utilização.
using WpfGrid = System.Windows.Controls.Grid;

namespace LeiriaDISIA.Views;

public partial class EquipamentosWindow : Window
{
    private List<Equipamento> _todos = new();

    /// <summary>Lista atualmente visível na DataGrid (já com o filtro/pesquisa aplicado). Guardada
    /// à parte para que o painel de resumo (cartões + gauges de obsolescência) seja calculado
    /// sobre exatamente os mesmos dados da grelha, sem repetir consultas à base de dados.</summary>
    private List<Equipamento> _visiveis = new();

    /// <summary>Capturado uma única vez no construtor (ver Services.JanelaCompactaService) — usado
    /// tanto para a escolha original entre os painéis Normal/Compacto da Obsolescência como, agora,
    /// pelas secções de distribuição dinâmica ("Sistemas Operativos", "Armazenamento" — cada uma
    /// decide sozinha, em código, entre gauges e barras finas, ver AtualizarDistribuicaoPorCampo),
    /// já que estas são únicas e não têm uma versão Compacta separada no XAML.</summary>
    private bool _modoCompacto;

    public EquipamentosWindow()
    {
        InitializeComponent();
        // Perfil Guest (Services/SessaoAtual.PodeEditar): acesso só de leitura a este módulo -
        // ver Services/PermissoesService.cs.
        LeiriaDISIA.Services.PermissoesService.AplicarSomenteLeituraSeGuest(BtnInserir);

        // O cabeçalho (cartões-resumo + gauges + legendas) tem um tamanho natural fixo (não muda
        // com o tamanho do ecrã), pensado para um monitor normal — em ecrãs mais baixos (ex.:
        // portáteis de 13"), reservar-lhe sempre metade da altura disponível (com um mínimo de
        // 280px, para não ficar ridiculamente pequeno nem em ecrãs minúsculos) garante que sobra
        // sempre espaço a sério para a lista de equipamento por baixo, com o cabeçalho a ganhar
        // scroll próprio se não couber tudo. Em monitores normais/grandes, o cabeçalho cabe
        // confortavelmente dentro desse teto e continua a aparecer por completo, sem scroll.
        ScrollCabecalho.MaxHeight = Math.Max(280, SystemParameters.WorkArea.Height * 0.5);

        // Modo Compacto (Administração → Aparência): troca os 3 gauges grandes por 3 barras finas
        // no painel "Distribuição por Obsolescência" — os gauges (140x140 cada) são o maior
        // consumidor de altura do cabeçalho a seguir aos cartões-resumo; em ecrãs pequenos, isto
        // devolve espaço a sério à lista de equipamento por baixo. Ambos os painéis são
        // atualizados em AtualizarResumo (ver mais abaixo) — só a visibilidade é decidida aqui,
        // uma única vez, na abertura da janela.
        if (Services.JanelaCompactaService.Ativo)
        {
            _modoCompacto = true;
            PainelObsolescenciaNormal.Visibility = Visibility.Collapsed;
            PainelObsolescenciaCompacto.Visibility = Visibility.Visible;
        }

        Recarregar();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Recarregar()
    {
        _todos = App.Db.Equipamentos.Include(e => e.Escola).ThenInclude(esc => esc!.Agrupamento).OrderByDescending(e => e.Id).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        var termo = TxtPesquisa?.Text?.Trim();
        _visiveis = string.IsNullOrWhiteSpace(termo)
            ? _todos
            : _todos.Where(e =>
                e.NumeroSerie.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                e.NumeroInventario.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                (e.Tipo?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Marca?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Escola?.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();

        Grid.ItemsSource = _visiveis;
        AtualizarResumo(_visiveis);
    }

    /// <summary>Atualiza o painel de resumo acima da DataGrid (cartões com totais dos principais
    /// tipos de equipamento + gauges com a distribuição percentual de obsolescência), a partir da
    /// MESMA lista já filtrada que alimenta a DataGrid — para os valores respeitarem sempre a
    /// pesquisa/filtro ativo, sem qualquer consulta adicional à base de dados.
    ///
    /// Os quatro tipos de equipamento e os três níveis de obsolescência usam exatamente os
    /// valores/nomes já existentes na aplicação: os tipos são os seedados em
    /// Data/DbInitializer.cs (grupo <see cref="GruposValorFixo.TipoEquipamento"/>, geridos em
    /// Administração → Dados Fixos) e os níveis vêm de <see cref="Equipamento.Obsolescencia"/>
    /// (ver <see cref="ObsolescenciaService"/>), a mesma classificação já usada no badge de
    /// "Obsolescência" da própria DataGrid.
    ///
    /// Os gauges reutilizam tal e qual o gauge circular já usado no Dashboard (mesmo controlo,
    /// mesmas cores/estilo) — ver <see cref="DashboardView.ConstruirGaugePercentagem"/>.</summary>
    private void AtualizarResumo(List<Equipamento> visiveis)
    {
        // ---- Linha 1: total geral + totais dos principais tipos de equipamento ----
        TxtTotalGeral.Text = visiveis.Count.ToString();
        TxtTotalSecretaria.Text = visiveis.Count(e => TipoEquivale(e.Tipo, "Computador de Secretária")).ToString();
        TxtTotalPortateis.Text = visiveis.Count(e => TipoEquivale(e.Tipo, "Portátil")).ToString();
        // "Access Point" usa Contains em vez de TipoEquivale (igualdade exata) porque o tipo
        // configurado em Dados Fixos pode legitimamente ter texto adicional a seguir (ex.: "Access
        // Points / Antena", tal como está configurado nesta instalação) — com igualdade exata, este
        // cartão ficava sempre a 0, mesmo havendo equipamento desse tipo visível na grelha por baixo.
        TxtTotalAccessPoints.Text = visiveis.Count(e => TipoContem(e.Tipo, "Access Point")).ToString();
        TxtTotalSwitches.Text = visiveis.Count(e => TipoEquivale(e.Tipo, "Switch")).ToString();

        AtualizarMaisIntervencionado(visiveis);

        // ---- Linha 2: gauges de obsolescência (% sobre o total de equipamento visível) ----
        var total = visiveis.Count;
        var totalAtual = visiveis.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.Atual);
        var totalMonitorizar = visiveis.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.AMonitorizar);
        var totalObsoleto = visiveis.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.Obsoleto);

        TxtTotalObsolescencia.Text = total == 0
            ? "% de equipamento em cada nível de obsolescência (sem equipamento a apresentar)"
            : $"% de equipamento em cada nível de obsolescência ({total} equipamentos)";

        // DashboardView.ConstruirGaugePercentagem já trata total == 0 devolvendo 0%, em vez de
        // dividir por zero (ver Views/DashboardView.xaml.cs).
        GaugeObsolescenciaAtual.Series = DashboardView.ConstruirGaugePercentagem(totalAtual, total, "#22C55E");
        TxtGaugeObsolescenciaAtual.Text = $"{totalAtual} / {total}";

        GaugeObsolescenciaMonitorizar.Series = DashboardView.ConstruirGaugePercentagem(totalMonitorizar, total, "#F59E0B");
        TxtGaugeObsolescenciaMonitorizar.Text = $"{totalMonitorizar} / {total}";

        GaugeObsolescenciaObsoleto.Series = DashboardView.ConstruirGaugePercentagem(totalObsoleto, total, "#EF4444");
        TxtGaugeObsolescenciaObsoleto.Text = $"{totalObsoleto} / {total}";

        // Versão compacta (Modo Compacto) do mesmo resumo — 3 barras finas em vez dos 3 gauges
        // acima. As colunas "Cheia"/"Vazia" de cada barra usam larguras em "*" (estrela) — que se
        // ajustam automaticamente à largura real do ecrã — em vez de pixels fixos, para a barra
        // preencher corretamente a percentagem certa seja qual for o tamanho da janela.
        AtualizarBarraCompacta(ColBarraAtualCheia, ColBarraAtualVazia, TxtBarraAtual, totalAtual, total);
        AtualizarBarraCompacta(ColBarraMonitorizarCheia, ColBarraMonitorizarVazia, TxtBarraMonitorizar, totalMonitorizar, total);
        AtualizarBarraCompacta(ColBarraObsoletoCheia, ColBarraObsoletoVazia, TxtBarraObsoleto, totalObsoleto, total);

        AtualizarDistribuicaoPorCampo(visiveis, e => e.SistemaOperativo,
            PainelGaugesSistemaOperativo, TxtTotalSistemasOperativos, TxtSemSistemasOperativos, "cada sistema operativo");
        AtualizarDistribuicaoPorCampo(visiveis, e => e.TipoDisco,
            PainelGaugesArmazenamento, TxtTotalArmazenamento, TxtSemArmazenamento, "cada tipo de armazenamento");
    }

    /// <summary>Cores usadas, por ordem, para os gauges/barras de distribuição dinâmica (Sistemas
    /// Operativos, Armazenamento, ...) — ao contrário da Obsolescência (sempre 3 níveis fixos, com
    /// cores fixas), o nº de valores distintos varia consoante o parque real, por isso não há uma
    /// cor "própria" de cada valor: usa-se sempre a próxima cor desta paleta, pela ordem em que
    /// aparecem (do mais para o menos comum), com "Outros" a ficar sempre em cinzento. Mesmos tons
    /// já usados noutros gráficos com categorias dinâmicas na aplicação (ver PaletaCategorias em
    /// Services/RelatorioService.cs).</summary>
    private static readonly string[] PaletaDistribuicaoDinamica =
    {
        "#1D4ED8", "#D97706", "#15803D", "#B91C1C", "#7E22CE", "#0F766E", "#BE185D", "#0369A1"
    };

    /// <summary>Agrupa o equipamento visível pelo valor devolvido por <paramref name="obterValor"/>
    /// (ex.: Sistema Operativo, Tipo de Disco) e desenha um gauge — ou, em Modo Compacto, uma barra
    /// fina — por cada valor distinto encontrado, com a % sobre o total de equipamento COM esse
    /// campo preenchido (não sobre o total geral de equipamento visível, que incluiria equipamento
    /// para o qual o campo nunca se aplica — ex.: um monitor não tem Sistema Operativo nem Tipo de
    /// Disco — e que por isso diluiria as percentagens sem necessidade). Para não sobrecarregar o
    /// painel com valores residuais (ex.: um único disco de um tipo raro já fora de uso), só os 6
    /// mais comuns aparecem individualmente — o resto (se houver) é somado num único "Outros".
    ///
    /// Partilhado entre "Sistemas Operativos" e "Armazenamento" (ver chamadas em
    /// AtualizarResumo) — parametrizado pelo campo a extrair e pelos controlos de destino, em vez
    /// de duas cópias quase iguais deste método. Ao contrário da Obsolescência (sempre 3 níveis
    /// fixos, já declarados no XAML), o número de valores distintos varia consoante o parque real,
    /// por isso os gauges/barras são construídos aqui, dinamicamente, em vez de existirem já fixos
    /// no XAML.</summary>
    private void AtualizarDistribuicaoPorCampo(List<Equipamento> visiveis, Func<Equipamento, string?> obterValor,
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
            ? $"% de equipamento com {descricaoCampo} (sem equipamento a apresentar)"
            : $"% de equipamento com {descricaoCampo} ({total} equipamentos)";

        txtSemDados.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;

        painelDestino.Children.Clear();

        for (var i = 0; i < grupos.Count; i++)
        {
            var (nome, parcela) = grupos[i];
            var cor = nome == "Outros" ? "#9CA3AF" : PaletaDistribuicaoDinamica[i % PaletaDistribuicaoDinamica.Length];

            if (_modoCompacto)
            {
                painelDestino.Children.Add(ConstruirBarraDistribuicao(nome, parcela, total, cor));
            }
            else
            {
                var painel = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 10, 10) };
                painel.Children.Add(new TextBlock
                {
                    Text = nome, Style = (Style)FindResource("KpiLabelStyle"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 130, TextAlignment = TextAlignment.Center
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
    /// de distribuição dinâmica ("Sistemas Operativos", "Armazenamento") — mesmo estilo visual das
    /// barras finas já usadas na versão compacta da Obsolescência (ver PainelObsolescenciaCompacto
    /// no XAML), mas montada em código porque o número de valores distintos é dinâmico (ali são
    /// sempre 3 barras fixas, declaradas no XAML).</summary>
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

    /// <summary>Ajusta a largura preenchida de uma barra fina do painel compacto de obsolescência
    /// (ver <see cref="AtualizarResumo"/>) para refletir a percentagem de <paramref name="parcela"/>
    /// sobre <paramref name="total"/>, e atualiza o texto "x / total" ao lado.</summary>
    private static void AtualizarBarraCompacta(ColumnDefinition colCheia, ColumnDefinition colVazia, TextBlock txt, int parcela, int total)
    {
        var percentagem = total == 0 ? 0 : (parcela * 100.0 / total);
        colCheia.Width = new GridLength(percentagem, GridUnitType.Star);
        colVazia.Width = new GridLength(100 - percentagem, GridUnitType.Star);
        txt.Text = $"{parcela} / {total}";
    }

    /// <summary>Compara o "Tipo" de um equipamento com um dos valores fixos seedados para o grupo
    /// <see cref="GruposValorFixo.TipoEquipamento"/> (ver Data/DbInitializer.cs), ignorando
    /// diferenças de maiúsculas/minúsculas — para não deixar de contar um equipamento só porque o
    /// texto foi escrito com uma capitalização ligeiramente diferente.</summary>
    private static bool TipoEquivale(string? tipo, string alvo) =>
        !string.IsNullOrWhiteSpace(tipo) && tipo.Equals(alvo, StringComparison.OrdinalIgnoreCase);

    /// <summary>Variante de <see cref="TipoEquivale"/> que verifica se o Tipo CONTÉM o trecho
    /// indicado, em vez de ser exatamente igual — usada para tipos cujo nome configurado em Dados
    /// Fixos pode legitimamente incluir texto adicional (ex.: "Access Points / Antena").</summary>
    private static bool TipoContem(string? tipo, string trecho) =>
        !string.IsNullOrWhiteSpace(tipo) && tipo.Contains(trecho, StringComparison.OrdinalIgnoreCase);

    /// <summary>Encontra, dentro da lista visível, o equipamento com mais intervenções somando as
    /// mesmas duas fontes já usadas no contador individual "Nº de Vezes Intervencionado" de cada
    /// equipamento (ver EquipamentoEditWindow.xaml.cs → CarregarHistoricoIntervencoes): intervenções
    /// no local (IntervencaoEquipamentos) e recolhas para a DISIA (EquipamentosRecolhidos). Os dois
    /// totais são pré-calculados de uma só vez por tabela (GroupBy), em vez de uma consulta por
    /// equipamento, para não repetir consultas à base de dados desnecessariamente com listas
    /// grandes.</summary>
    private void AtualizarMaisIntervencionado(List<Equipamento> visiveis)
    {
        if (visiveis.Count == 0) { PainelMaisIntervencionado.Visibility = Visibility.Collapsed; return; }

        var idsVisiveis = visiveis.Select(e => e.Id).ToHashSet();

        var porLocal = App.Db.IntervencaoEquipamentos
            .Where(ie => idsVisiveis.Contains(ie.EquipamentoId))
            .GroupBy(ie => ie.EquipamentoId)
            .Select(g => new { EquipamentoId = g.Key, Total = g.Count() })
            .ToDictionary(x => x.EquipamentoId, x => x.Total);

        var porRecolha = App.Db.EquipamentosRecolhidos
            .Where(r => idsVisiveis.Contains(r.EquipamentoId))
            .GroupBy(r => r.EquipamentoId)
            .Select(g => new { EquipamentoId = g.Key, Total = g.Count() })
            .ToDictionary(x => x.EquipamentoId, x => x.Total);

        // Total de intervenções por equipamento — não inclui nenhuma restrição de data (todos os
        // anos, sempre) — usado apenas para o destaque do EQUIPAMENTO em concreto, que só pode
        // contar intervenções com esse equipamento associado (é isso que está a comparar).
        var totalPorEquipamento = visiveis
            .Select(e => new { Equipamento = e, Total = porLocal.GetValueOrDefault(e.Id) + porRecolha.GetValueOrDefault(e.Id) })
            .Where(x => x.Total > 0)
            .ToList();

        if (totalPorEquipamento.Count == 0)
        {
            PainelMaisIntervencionado.Visibility = Visibility.Collapsed;
            return;
        }

        PainelMaisIntervencionado.Visibility = Visibility.Visible;

        var maisIntervencionado = totalPorEquipamento.OrderByDescending(x => x.Total).First();
        var eq = maisIntervencionado.Equipamento;
        var vezesEquipamento = maisIntervencionado.Total == 1 ? "1 intervenção" : $"{maisIntervencionado.Total} intervenções";
        TxtMaisIntervencionado.Text = $"{eq.NumeroSerie} — {eq.Tipo} ({eq.Escola?.Nome ?? "sem escola associada"}) — {vezesEquipamento}";

        // Escola/Agrupamento com mais equipamentos intervencionados: soma o total já calculado
        // acima por equipamento (porLocal + porRecolha), agrupado por escola/agrupamento — não
        // conta equipamentos distintos, soma mesmo o total de intervenções em equipamento: uma
        // intervenção que tenha tocado em 10 computadores diferentes nessa escola conta 10 (porque
        // é isso que fica registado em Intervencao → Equipamentos dessa intervenção), e um mesmo
        // equipamento intervencionado em 5 visitas distintas também conta 5. Só entram equipamentos
        // com intervenções associadas (o mesmo "totalPorEquipamento" já filtrado acima) — por isso
        // esta contagem é sempre só sobre EQUIPAMENTO, nunca sobre intervenções gerais sem nenhum
        // equipamento associado.
        var escolaMaisIntervencionada = totalPorEquipamento
            .Where(x => x.Equipamento.EscolaId != null)
            .GroupBy(x => x.Equipamento.EscolaId!.Value)
            .Select(g => new { Escola = g.First().Equipamento.Escola!, Total = g.Sum(x => x.Total) })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();

        if (escolaMaisIntervencionada != null)
        {
            var vezesEscola = escolaMaisIntervencionada.Total == 1 ? "1 equipamento intervencionado" : $"{escolaMaisIntervencionada.Total} equipamentos intervencionados";
            TxtEscolaMaisIntervencionada.Text = $"{escolaMaisIntervencionada.Escola.Nome} — {vezesEscola}";
            PainelEscolaMaisIntervencionada.Visibility = Visibility.Visible;
        }
        else
        {
            PainelEscolaMaisIntervencionada.Visibility = Visibility.Collapsed;
        }

        // Agrupamento com mais equipamentos intervencionados: idem, agora agrupado pelo
        // agrupamento da escola de cada equipamento.
        var agrupamentoMaisIntervencionado = totalPorEquipamento
            .Where(x => x.Equipamento.Escola?.AgrupamentoId != null)
            .GroupBy(x => x.Equipamento.Escola!.AgrupamentoId!.Value)
            .Select(g => new { Agrupamento = g.First().Equipamento.Escola!.Agrupamento!, Total = g.Sum(x => x.Total) })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();

        if (agrupamentoMaisIntervencionado != null)
        {
            var vezesAgrupamento = agrupamentoMaisIntervencionado.Total == 1 ? "1 equipamento intervencionado" : $"{agrupamentoMaisIntervencionado.Total} equipamentos intervencionados";
            TxtAgrupamentoMaisIntervencionado.Text = $"{agrupamentoMaisIntervencionado.Agrupamento.Nome} — {vezesAgrupamento}";
            PainelAgrupamentoMaisIntervencionado.Visibility = Visibility.Visible;
        }
        else
        {
            PainelAgrupamentoMaisIntervencionado.Visibility = Visibility.Collapsed;
        }
    }

    private void Filtro_Changed(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new EquipamentoEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not Equipamento equipamento) return;

        var janela = new EquipamentoEditWindow(equipamento) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        // (1.1) O relatório do módulo reflete exatamente o que está a ser visto na grelha — se
        // houver uma pesquisa/filtro ativo, só esse subconjunto entra no relatório. Sem isto,
        // "Relatório do Módulo" gerava sempre o inventário completo, independentemente do que
        // estava filtrado no ecrã, o que não corresponde ao que o botão sugere.
        if (_visiveis.Count == 0)
        {
            MessageBox.Show("Não existem equipamentos a corresponder ao filtro atual para incluir no relatório.",
                "Sem dados para o relatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de equipamento informático",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Equipamento_Informatico_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaEquipamento(dialog.FileName, _visiveis.Select(eq => eq.Id).ToList());

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

    // Item 3.3: aberta a partir daqui (e não de um hipotético "Relatórios → Equipamentos" central,
    // que não existe nesta aplicação — cada módulo tem o seu próprio botão de relatório) porque é
    // exatamente aqui que o utilizador já está no contexto de um equipamento/escola específicos
    // (pode inclusive já ter pesquisado por uma escola em TxtPesquisa antes de clicar), o que
    // corresponde à preferência indicada: "preferencialmente no módulo Equipamentos Informáticos,
    // se o utilizador puder selecionar uma escola/contexto antes de gerar".
    private void FolhaInventario_Click(object sender, RoutedEventArgs e)
    {
        var janela = new FolhaInventarioWindow { Owner = this };
        janela.ShowDialog();
    }

    private void ModelosEquipamento_Click(object sender, RoutedEventArgs e)
    {
        var janela = new ModelosEquipamentoWindow { Owner = this };
        janela.ShowDialog();
    }
}
