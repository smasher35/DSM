using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LeiriaDISIA.Services;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Extensions;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace LeiriaDISIA.Views;

public partial class DashboardView : UserControl
{
    // Anteriormente este parâmetro era recebido mas nunca chegava a ser usado — todos os gráficos
    // ficavam sempre com animação de ~1,5s. Isso passava despercebido no ecrã normal, mas na
    // captura para o Relatório Mensal (ver DashboardSnapshotService) a imagem era tirada muito
    // antes de a animação terminar: os gauges (cujo arco de fundo é desenhado de imediato) já
    // apareciam completos, mas as barras/gráficos (que crescem a partir de 0) ainda estavam
    // invisíveis nesse instante — daí "só aparecerem os gauges".
    private readonly TimeSpan _velocidadeAnimacao;

    // Indica se as ligações entre os cartões-invólucro FHD/UHD e o conteúdo (RegistarMapaResolucao)
    // já foram estabelecidas, para AplicarResolucao poder ser chamado em segurança a qualquer altura.
    private bool _mapaResolucaoPronto;

    /// <summary>Guarda o resumo carregado por <see cref="Carregar"/>, para <see cref="LeiriaDISIA.Services.DashboardSnapshotService.CapturarSeccoes"/>
    /// poder desenhar os gráficos "Intervenções por Agrupamento" diretamente a partir dos dados —
    /// em vez de tirar um screenshot do controlo LiveCharts já desenhado no ecrã (ver
    /// <see cref="RenderizarGraficoAgrupamento"/> para o porquê desta troca).</summary>
    internal DashboardResumo? UltimoResumo { get; private set; }

    // Um par (invólucro FHD, invólucro UHD) por cada cartão do Dashboard. O conteúdo real (o
    // StackPanel com os controlos com x:Name usados por Carregar()) começa sempre dentro do
    // invólucro FHD (é assim que o XAML o define) e é movido para o invólucro UHD, e vice-versa,
    // consoante a resolução escolhida — nunca duplicado.
    private readonly List<(Border Fhd, Border Uhd)> _cartoes = new();

    /// <summary>Alturas/larguras originais (FHD) dos gráficos e gauges, para poderem ser
    /// reduzidas de forma consistente em UHD e sempre restauradas em FHD sem perder o valor
    /// validado original.</summary>
    private const double AlturaGaugeFhd = 150, AlturaGaugeUhd = 112;
    private const double AlturaChartPorMesFhd = 260, AlturaChartPorMesUhd = 230;
    private const double AlturaChartPieFhd = 280, AlturaChartPieUhd = 240;
    private const double AlturaChartAgrupamentoFhd = 260, AlturaChartAgrupamentoUhd = 228;
    private const double AlturaChartCategoriaAgrupamentoFhd = 280, AlturaChartCategoriaAgrupamentoUhd = 240;
    private const double AlturaChartPendentesFhd = 240, AlturaChartPendentesUhd = 220;

    public DashboardView(bool desativarAnimacoes = false)
    {
        InitializeComponent();
        _velocidadeAnimacao = desativarAnimacoes ? TimeSpan.Zero : TimeSpan.FromMilliseconds(1500);

        RegistarMapaResolucao();
        Carregar();

        // Reaplica de imediato a resolução escolhida em Administração → Aparência (guardada entre
        // sessões). Em FHD não há nada a fazer — é a disposição em que o XAML já nasce.
        if (DashboardResolucaoService.UhdAtivo)
            AplicarResolucao(uhd: true);

        // Enquanto este Dashboard estiver visível, reage de imediato se o utilizador mudar a
        // resolução em Administração → Aparência (sem precisar de reabrir o módulo Dashboard).
        // A subscrição é removida em Unloaded para não prender esta instância em memória depois
        // de o utilizador navegar para outro módulo (ver MainWindow: cada navegação cria uma
        // instância nova de DashboardView).
        DashboardResolucaoService.ResolucaoMudou += ResolucaoMudou_Handler;
        Unloaded += (_, _) => DashboardResolucaoService.ResolucaoMudou -= ResolucaoMudou_Handler;
    }

    private void ResolucaoMudou_Handler(object? sender, bool uhd) => AplicarResolucao(uhd);

    /// <summary>Associa cada par de invólucros (cartão FHD ↔ cartão UHD equivalente). Chamado uma
    /// única vez, no arranque. Esta lista é o único sítio onde a correspondência entre as duas
    /// disposições está definida — para acrescentar/remover um cartão do Dashboard no futuro,
    /// basta nomear os dois invólucros (Border) no XAML e acrescentar/remover aqui uma linha.</summary>
    private void RegistarMapaResolucao()
    {
        _cartoes.Clear();
        _cartoes.Add((CardFhd_TotalGlobal, CardUhd_TotalGlobal));
        _cartoes.Add((CardFhd_TotalAno, CardUhd_TotalAno));
        _cartoes.Add((CardFhd_TotalMes, CardUhd_TotalMes));
        _cartoes.Add((CardFhd_AtividadesNaoConcluidas, CardUhd_AtividadesNaoConcluidas));
        _cartoes.Add((CardFhd_Pendentes, CardUhd_Pendentes));
        _cartoes.Add((CardFhd_TopEscola, CardUhd_TopEscola));
        _cartoes.Add((CardFhd_TopAgrupamento, CardUhd_TopAgrupamento));
        _cartoes.Add((CardFhd_Agrupamentos, CardUhd_Agrupamentos));
        _cartoes.Add((CardFhd_Escolas, CardUhd_Escolas));
        _cartoes.Add((CardFhd_Edificios, CardUhd_Edificios));
        _cartoes.Add((CardFhd_JiIntegrados, CardUhd_JiIntegrados));
        _cartoes.Add((CardFhd_JiIsolados, CardUhd_JiIsolados));
        _cartoes.Add((CardFhd_TotalComputadores, CardUhd_TotalComputadores));
        _cartoes.Add((CardFhd_ComputadoresRecolhidos, CardUhd_ComputadoresRecolhidos));
        _cartoes.Add((CardFhd_ComputadoresAguardamEntrega, CardUhd_ComputadoresAguardamEntrega));
        _cartoes.Add((CardFhd_EquipamentoObsoleto, CardUhd_EquipamentoObsoleto));
        _cartoes.Add((CardFhd_Gauges, CardUhd_Gauges));
        _cartoes.Add((CardFhd_ChartPorMes, CardUhd_ChartPorMes));
        _cartoes.Add((CardFhd_ChartPorCategoria, CardUhd_ChartPorCategoria));
        _cartoes.Add((CardFhd_ChartPorCategoriaMes, CardUhd_ChartPorCategoriaMes));
        _cartoes.Add((CardFhd_ChartAgrupamentoAno, CardUhd_ChartAgrupamentoAno));
        _cartoes.Add((CardFhd_ChartAgrupamentoMes, CardUhd_ChartAgrupamentoMes));
        _cartoes.Add((CardFhd_ChartCategoriaAgrupamentoAno, CardUhd_ChartCategoriaAgrupamentoAno));
        _cartoes.Add((CardFhd_ChartCategoriaAgrupamentoMes, CardUhd_ChartCategoriaAgrupamentoMes));
        _cartoes.Add((CardFhd_ChartPendentes, CardUhd_ChartPendentes));
        _mapaResolucaoPronto = true;
    }

    /// <summary>Troca entre a disposição FHD (validada, original) e a disposição UHD (compacta):
    /// move o conteúdo de cada cartão para o invólucro correspondente na disposição escolhida e
    /// alterna qual dos dois ScrollViewer fica visível. Não recalcula nem altera nenhum dado —
    /// os mesmos controlos (com os mesmos x:Name, os mesmos valores já carregados por Carregar())
    /// continuam a existir, apenas com um "pai" visual diferente.</summary>
    private void AplicarResolucao(bool uhd)
    {
        if (!_mapaResolucaoPronto) return;

        foreach (var (fhd, uhdBorder) in _cartoes)
        {
            // Cada cartão só tem, de cada vez, um dos dois invólucros com Child != null: é esse
            // que contém o conteúdo real. Move-o para o invólucro do lado pretendido (se já lá
            // estiver, esta operação não tem qualquer efeito).
            var conteudo = fhd.Child ?? uhdBorder.Child;
            if (conteudo is null) continue;

            var destino = uhd ? uhdBorder : fhd;
            var origem = destino == fhd ? uhdBorder : fhd;
            if (ReferenceEquals(origem.Child, conteudo))
            {
                origem.Child = null;
                destino.Child = conteudo;
            }
        }

        AjustarTamanhosGraficos(uhd);

        ScrollFhd.Visibility = uhd ? Visibility.Collapsed : Visibility.Visible;
        ScrollUhd.Visibility = uhd ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Reduz (UHD) ou repõe (FHD) as alturas/larguras dos gauges e gráficos. Os valores
    /// usados em FHD são exatamente os valores originais, validados, definidos no XAML — nunca
    /// alterados; os valores em UHD são propositadamente mais compactos, mas sem prejudicar a
    /// leitura, conforme pedido.</summary>
    private void AjustarTamanhosGraficos(bool uhd)
    {
        var alturaGauge = uhd ? AlturaGaugeUhd : AlturaGaugeFhd;
        GaugeTotalComputadores.Width = GaugeTotalComputadores.Height = alturaGauge;
        GaugeComputadoresSecretaria.Width = GaugeComputadoresSecretaria.Height = alturaGauge;
        GaugePortateis.Width = GaugePortateis.Height = alturaGauge;
        GaugeSwitches.Width = GaugeSwitches.Height = alturaGauge;
        GaugeAccessPoints.Width = GaugeAccessPoints.Height = alturaGauge;
        GaugeImpressoras.Width = GaugeImpressoras.Height = alturaGauge;

        ChartPorMes.Height = uhd ? AlturaChartPorMesUhd : AlturaChartPorMesFhd;
        ChartPorCategoria.Height = uhd ? AlturaChartPieUhd : AlturaChartPieFhd;
        ChartPorCategoriaMes.Height = uhd ? AlturaChartPieUhd : AlturaChartPieFhd;
        ChartAgrupamentoAno.Height = uhd ? AlturaChartAgrupamentoUhd : AlturaChartAgrupamentoFhd;
        ChartAgrupamentoMes.Height = uhd ? AlturaChartAgrupamentoUhd : AlturaChartAgrupamentoFhd;
        ChartCategoriaAgrupamentoAno.Height = uhd ? AlturaChartCategoriaAgrupamentoUhd : AlturaChartCategoriaAgrupamentoFhd;
        ChartCategoriaAgrupamentoMes.Height = uhd ? AlturaChartCategoriaAgrupamentoUhd : AlturaChartCategoriaAgrupamentoFhd;
        ChartPendentes.Height = uhd ? AlturaChartPendentesUhd : AlturaChartPendentesFhd;
    }

    /// <summary>Uma imagem PNG (ou null, se a captura dessa secção falhar/não tiver conteúdo) por
    /// cada gráfico do Dashboard usado no Relatório Mensal (ver
    /// <see cref="LeiriaDISIA.Services.DashboardSnapshotService.CapturarSeccoes"/>) — recortadas de
    /// uma única captura completa do Dashboard, para o relatório poder organizar estas secções em
    /// linhas compactas (2 gráficos lado a lado), tal como aparecem no ecrã, em vez de as espremer
    /// todas na largura de uma única coluna.</summary>
    public sealed record DashboardSeccoesSnapshot(
        byte[]? Kpis,
        byte[]? Gauges,
        byte[]? ChartPorMes,
        byte[]? ChartPorCategoriaAno,
        byte[]? ChartPorCategoriaMes,
        byte[]? ChartAgrupamentoAno,
        byte[]? ChartAgrupamentoMes,
        string? LegendaChartAgrupamentoAno = null,
        string? LegendaChartAgrupamentoMes = null);

    /// <summary>Captura, em separado, cada uma das secções do Dashboard (KPIs, gauges do parque de
    /// equipamento, intervenções por mês, intervenções por categoria — ano e mês — e intervenções
    /// por agrupamento — ano e mês), já com os dados mais recentes carregados por
    /// <see cref="Carregar"/>. Chamado depois de a janela auxiliar de captura (ver
    /// <see cref="LeiriaDISIA.Services.DashboardSnapshotService"/>) já ter feito o layout e
    /// aguardado os ciclos de render necessários — tal como <see cref="LeiriaDISIA.Services.DashboardSnapshotService.Capturar"/>.
    ///
    /// Em vez de renderizar cada cartão isoladamente (uma tentativa anterior fazia
    /// <c>RenderTargetBitmap.Render(cartao)</c> diretamente em cada Border), o que produzia
    /// capturas incompletas/deslocadas para os gráficos LiveCharts/SkiaSharp quando não são a raiz
    /// da árvore visual capturada, esta versão renderiza o Dashboard inteiro UMA ÚNICA VEZ — a
    /// mesma operação, comprovadamente correta, que <see cref="LeiriaDISIA.Services.DashboardSnapshotService.Capturar"/>
    /// sempre usou — e depois recorta dessa única imagem a região exata de cada cartão, usando a
    /// posição de cada um relativamente ao Dashboard (<see cref="UIElement.TransformToAncestor"/>).</summary>
    public DashboardSeccoesSnapshot CapturarSeccoes()
    {
        var larguraReal = (int)Math.Ceiling(ActualWidth);
        var alturaReal = (int)Math.Ceiling(ActualHeight);
        if (larguraReal <= 0 || alturaReal <= 0)
            return new DashboardSeccoesSnapshot(null, null, null, null, null, null, null);

        RenderTargetBitmap bitmapCompleto;
        try
        {
            bitmapCompleto = new RenderTargetBitmap(larguraReal, alturaReal, 96, 96, PixelFormats.Pbgra32);
            bitmapCompleto.Render(this);
        }
        catch
        {
            return new DashboardSeccoesSnapshot(null, null, null, null, null, null, null);
        }

        // Em cada resolução só um dos dois invólucros (FHD/UHD) de cada par tem conteúdo — ver
        // AplicarResolucao — mas usa-se a visibilidade do ScrollViewer correspondente (em vez de
        // "Child != null") para escolher, porque também se aplica ao painel de KPIs, cujo
        // contentor (PainelKpisFhd/GridUhdKpis) nunca é trocado, apenas os cartões lá dentro.
        var fhdAtivo = ScrollFhd.Visibility == Visibility.Visible;

        FrameworkElement? Escolher(FrameworkElement fhd, FrameworkElement uhd) => fhdAtivo ? fhd : uhd;

        // "Intervenções por Agrupamento" (ano/mês) NÃO é capturado por screenshot como as
        // restantes secções — é desenhado diretamente a partir dos dados (ver
        // LeiriaDISIA.Services.DashboardSnapshotService.RenderizarGraficoAgrupamento), porque o
        // número de barras muda todos os meses e a captura de ecrã deste gráfico em concreto
        // revelou-se pouco fiável (gráfico vazio ou com barras em falta, consoante o timing do
        // redesenho "throttled" do LiveCharts). A legenda (abreviatura = nome completo do
        // agrupamento) é escrita à parte, como texto normal do PDF — ver ComposeGestaoSiga.
        var graficoAno = DashboardSnapshotService.RenderizarGraficoAgrupamento(UltimoResumo?.IntervencoesPorAgrupamentoAnoCorrente ?? new());
        var graficoMes = DashboardSnapshotService.RenderizarGraficoAgrupamento(UltimoResumo?.IntervencoesPorAgrupamentoMesCorrente ?? new());

        return new DashboardSeccoesSnapshot(
            Kpis: RecortarCartao(bitmapCompleto, Escolher(PainelKpisFhd, GridUhdKpis)),
            Gauges: RecortarCartao(bitmapCompleto, Escolher(CardFhd_Gauges, CardUhd_Gauges)),
            ChartPorMes: RecortarCartao(bitmapCompleto, Escolher(CardFhd_ChartPorMes, CardUhd_ChartPorMes)),
            ChartPorCategoriaAno: RecortarCartao(bitmapCompleto, Escolher(CardFhd_ChartPorCategoria, CardUhd_ChartPorCategoria)),
            ChartPorCategoriaMes: RecortarCartao(bitmapCompleto, Escolher(CardFhd_ChartPorCategoriaMes, CardUhd_ChartPorCategoriaMes)),
            ChartAgrupamentoAno: graficoAno,
            ChartAgrupamentoMes: graficoMes,
            LegendaChartAgrupamentoAno: UltimoResumo is null ? null : ConstruirLegendaAbreviaturas(UltimoResumo.IntervencoesPorAgrupamentoAnoCorrente),
            LegendaChartAgrupamentoMes: UltimoResumo is null ? null : ConstruirLegendaAbreviaturas(UltimoResumo.IntervencoesPorAgrupamentoMesCorrente));
    }

    /// <summary>Recorta, a partir da captura completa do Dashboard (<paramref name="bitmapCompleto"/>),
    /// a região exata correspondente a <paramref name="elemento"/> (incluindo o seu fundo/moldura,
    /// tal como aparece no ecrã) e devolve-a como PNG. Devolve null se o elemento estiver vazio,
    /// invisível (ex. um cartão de um par FHD/UHD que não está ativo) ou a operação falhar por
    /// qualquer motivo — o relatório continua a ser gerado na mesma, apenas sem essa imagem.
    ///
    /// A altura do recorte é a MAIOR entre vários sinais possíveis, em vez de confiar cegamente
    /// num só (o que, em alguns cartões, se revelou pouco fiável e cortava o recorte a meio):
    /// (1) a própria caixa de layout do elemento (<c>ActualWidth</c>/<c>ActualHeight</c>);
    /// (2) <see cref="VisualTreeHelper.GetDescendantBounds"/>, a área efetivamente pintada pelos
    /// descendentes, que pode ultrapassar (1) em cartões com texto de legenda cuja altura final só
    /// fica definida depois de a largura da coluna estar resolvida;
    /// (3) quando indicado, a distância vertical entre o topo de <paramref name="elemento"/> e o
    /// topo de <paramref name="proximoElemento"/> (a secção seguinte no ecrã) — só depende da
    /// posição de dois pontos, não do tamanho que o próprio cartão diz ter;
    /// (4) quando indicada, <paramref name="alturaMinima"/> — um piso de segurança calculado a
    /// partir de constantes conhecidas (ver <see cref="CapturarSeccoes"/>), para cartões em que nem
    /// (1), (2) nem (3) se mostraram fiáveis.
    ///
    /// Usar sempre a MAIOR das medições disponíveis nunca reduz o recorte de um cartão que já
    /// estava correto — só o alarga nos casos em que alguma das medições ficava aquém do real.</summary>
    private byte[]? RecortarCartao(RenderTargetBitmap bitmapCompleto, FrameworkElement? elemento,
        FrameworkElement? proximoElemento = null, double alturaMinima = 0)
    {
        if (elemento is null || elemento.ActualWidth <= 0 || elemento.ActualHeight <= 0) return null;

        try
        {
            var topoAtual = elemento.TransformToAncestor(this).Transform(new System.Windows.Point(0, 0));

            var caixaLayout = new Rect(0, 0, elemento.ActualWidth, elemento.ActualHeight);
            var caixaConteudo = VisualTreeHelper.GetDescendantBounds(elemento);
            var origemLocal = caixaConteudo.IsEmpty ? caixaLayout : Rect.Union(caixaLayout, caixaConteudo);

            var largura = origemLocal.Width;
            var altura = origemLocal.Height;

            if (proximoElemento is { ActualHeight: > 0 })
            {
                var topoProximo = proximoElemento.TransformToAncestor(this).Transform(new System.Windows.Point(0, 0));
                var alturaAteProximo = topoProximo.Y - topoAtual.Y - origemLocal.Y;
                if (alturaAteProximo > altura) altura = alturaAteProximo;
            }

            if (alturaMinima > altura) altura = alturaMinima;

            var bounds = elemento.TransformToAncestor(this)
                .TransformBounds(new Rect(origemLocal.X, origemLocal.Y, largura, altura));

            var x = Math.Max(0, (int)Math.Floor(bounds.X));
            var y = Math.Max(0, (int)Math.Floor(bounds.Y));
            var larguraFinal = Math.Min(bitmapCompleto.PixelWidth - x, (int)Math.Ceiling(bounds.Width));
            var alturaFinal = Math.Min(bitmapCompleto.PixelHeight - y, (int)Math.Ceiling(bounds.Height));
            if (larguraFinal <= 0 || alturaFinal <= 0) return null;

            return CodificarRecortePng(bitmapCompleto, x, y, larguraFinal, alturaFinal);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Recorta a região indicada de <paramref name="bitmapCompleto"/> (em píxeis) e
    /// devolve-a codificada como PNG.</summary>
    private static byte[] CodificarRecortePng(RenderTargetBitmap bitmapCompleto, int x, int y, int largura, int altura)
    {
        var recorte = new CroppedBitmap(bitmapCompleto, new Int32Rect(x, y, largura, altura));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(recorte));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Paleta de cores usada para distinguir visualmente cada barra dos gráficos de
    /// barras do dashboard. Repete-se em ciclo se houver mais barras do que cores.</summary>
    internal static readonly string[] PaletaBarras =
    {
        "#1F4E79", "#2AB7CA", "#F59E0B", "#22C55E", "#EF4444",
        "#8B5CF6", "#EC4899", "#14B8A6", "#F97316", "#6366F1",
        "#84CC16", "#0EA5E9", "#D946EF", "#EAB308", "#10B981"
    };

    /// <summary>Escolhe branco ou preto para o texto consoante a luminância da cor de fundo
    /// indicada, garantindo que os valores embutidos em cada barra/fatia do gráfico ficam sempre
    /// legíveis, seja qual for a cor da série.</summary>
    internal static SKColor CorDeTextoContrastante(string corFundoHex)
    {
        var cor = SKColor.Parse(corFundoHex);
        var luminancia = (0.299 * cor.Red + 0.587 * cor.Green + 0.114 * cor.Blue) / 255.0;
        return luminancia > 0.6 ? SKColors.Black : SKColors.White;
    }

    /// <summary>Constrói um gauge circular (estilo "% usage") com a percentagem de <paramref name="valor"/>
    /// face a <paramref name="total"/>: um arco colorido com a percentagem, sobre um arco de fundo
    /// cinzento que representa os 100%. Usado na linha "Distribuição do Parque de Equipamento".
    /// Internal (em vez de private) para poder ser reutilizado tal e qual pelos gauges de
    /// obsolescência do módulo Equipamentos — ver Views/EquipamentosWindow.xaml.cs — mantendo o
    /// mesmo padrão visual/técnico em toda a aplicação, sem duplicar a lógica de construção do gauge.</summary>
    internal static ISeries[] ConstruirGaugePercentagem(int valor, int total, string corHex)
    {
        var percentagem = total > 0 ? Math.Round(valor * 100.0 / total, 1) : 0;
        return GaugeGenerator.BuildSolidGauge(
            new GaugeItem(percentagem, series =>
            {
                series.Fill = new SolidColorPaint(SKColor.Parse(corHex));
                series.InnerRadius = 45;
                series.DataLabelsPosition = PolarLabelsPosition.ChartCenter;
                series.DataLabelsPaint = new SolidColorPaint(SKColor.Parse(corHex))
                {
                    SKTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                };
                series.DataLabelsSize = 20;
                series.DataLabelsFormatter = point => $"{point.Coordinate.PrimaryValue:0}%";
            }),
            new GaugeItem(GaugeItem.Background, series =>
            {
                series.InnerRadius = 45;
                series.Fill = new SolidColorPaint(new SKColor(140, 140, 140, 50));
            })).ToArray();
    }

    /// <summary>1.3: pincel usado nos valores embutidos em cada série dos gráficos do Dashboard —
    /// maior e a negrito, com a cor de texto ajustada ao contraste da cor de fundo da barra/fatia,
    /// para garantir boa legibilidade e impacto visual independentemente da cor da série.</summary>
    private static SolidColorPaint PaintValorEmbutido(string corFundoHex) => new(CorDeTextoContrastante(corFundoHex))
    {
        SKTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
    };

    /// <summary>Tamanho de letra (aumentado, cf. 1.3) usado nos valores embutidos nas séries dos gráficos.</summary>
    internal const double TamanhoValorEmbutido = 14;

    /// <summary>Constrói uma série de colunas por cada valor, cada uma com apenas um ponto visível
    /// (os restantes ficam a null, logo não são desenhados), o que permite atribuir uma cor
    /// diferente a cada barra do gráfico em vez de todas partilharem a mesma cor. Cada barra mostra
    /// ainda o seu próprio valor embutido, com a cor de texto ajustada ao contraste da barra.
    ///
    /// Interno (não privado) para poder ser reutilizada por
    /// <see cref="LeiriaDISIA.Services.DashboardSnapshotService.RenderizarGraficoAgrupamento"/>, que
    /// desenha o gráfico "Intervenções por Agrupamento" diretamente para o Relatório Mensal (fora do
    /// ecrã, sem depender de nenhum controlo WPF já desenhado) — para o resultado ser sempre
    /// visualmente igual ao gráfico equivalente no ecrã do Dashboard.</summary>
    internal static ISeries[] SeriesColoridasPorBarra(IReadOnlyList<int> valores, IReadOnlyList<string>? nomes = null)
    {
        var series = new ISeries[valores.Count];
        for (var i = 0; i < valores.Count; i++)
        {
            var pontos = new double?[valores.Count];
            pontos[i] = valores[i];
            var corBarra = PaletaBarras[i % PaletaBarras.Length];

            series[i] = new ColumnSeries<double?>
            {
                Values = pontos,
                Name = nomes != null && i < nomes.Count ? nomes[i] : $"Barra {i + 1}",
                Fill = new SolidColorPaint(SKColor.Parse(corBarra)),
                IgnoresBarPosition = true,
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Middle,
                DataLabelsPaint = PaintValorEmbutido(corBarra),
                DataLabelsSize = TamanhoValorEmbutido,
                DataLabelsFormatter = point => point.Coordinate.PrimaryValue > 0 ? $"{point.Coordinate.PrimaryValue:0}" : ""
            };
        }
        return series;
    }

    /// <summary>Constrói o texto de legenda apresentado por baixo dos gráficos de barras por
    /// agrupamento, mapeando cada abreviatura usada no gráfico ao nome completo do agrupamento.
    /// Entradas em que a abreviatura é igual ao nome (ex.: "(Sem Agrupamento)") são omitidas,
    /// já que não precisam de tradução.</summary>
    private static string ConstruirLegendaAbreviaturas(IEnumerable<(string Agrupamento, string Abreviatura, int Total)> dados)
    {
        var pares = dados
            .Where(d => !string.Equals(d.Abreviatura, d.Agrupamento, StringComparison.OrdinalIgnoreCase))
            .Select(d => $"{d.Abreviatura} = {d.Agrupamento}");
        return string.Join("     ", pares);
    }

    /// <summary>Duração das animações de entrada dos gráficos do dashboard (valor mais alto = animação mais lenta e com mais impacto visual).</summary>

    private void Carregar()
    {
        // Aplica a velocidade de animação a todos os gráficos do dashboard.
        GaugeTotalComputadores.AnimationsSpeed        = _velocidadeAnimacao;
        GaugeComputadoresSecretaria.AnimationsSpeed  = _velocidadeAnimacao;
        GaugePortateis.AnimationsSpeed               = _velocidadeAnimacao;
        GaugeSwitches.AnimationsSpeed                = _velocidadeAnimacao;
        GaugeAccessPoints.AnimationsSpeed            = _velocidadeAnimacao;
        GaugeImpressoras.AnimationsSpeed             = _velocidadeAnimacao;
        ChartPorMes.AnimationsSpeed                  = _velocidadeAnimacao;
        ChartPorCategoria.AnimationsSpeed            = _velocidadeAnimacao;
        ChartPorCategoriaMes.AnimationsSpeed         = _velocidadeAnimacao;
        ChartAgrupamentoAno.AnimationsSpeed          = _velocidadeAnimacao;
        ChartAgrupamentoMes.AnimationsSpeed          = _velocidadeAnimacao;
        ChartCategoriaAgrupamentoAno.AnimationsSpeed = _velocidadeAnimacao;
        ChartCategoriaAgrupamentoMes.AnimationsSpeed = _velocidadeAnimacao;
        ChartPendentes.AnimationsSpeed               = _velocidadeAnimacao;

        var ano = DateTime.Today.Year;
        var servico = new DashboardService(App.Db);
        var resumo = servico.Gerar(ano);

        TxtAno.Text = $"— {ano}";
        KpiTotalAno.Text = resumo.TotalIntervencoesAnoCorrente.ToString();
        KpiTotalMes.Text = resumo.TotalIntervencoesMesCorrente.ToString();
        KpiTotalGlobal.Text = resumo.TotalIntervencoesGlobal.ToString();
        KpiAgrupamentos.Text = resumo.TotalAgrupamentos.ToString();
        KpiEscolas.Text = resumo.TotalEscolas.ToString();
        KpiPendentes.Text = resumo.PedidosNaoConcluidos.ToString();
        KpiAtividadesNaoConcluidas.Text = resumo.AtividadesDisiaNaoConcluidas.ToString();
        KpiEdificios.Text = resumo.TotalEdificios.ToString();
        KpiJiIntegrados.Text = resumo.TotalJiIntegrados.ToString();
        KpiJiIsolados.Text = resumo.TotalJiIsolados.ToString();
        KpiTotalComputadores.Text = resumo.TotalComputadores.ToString();
        KpiComputadoresRecolhidos.Text = resumo.TotalComputadoresRecolhidos.ToString();
        KpiComputadoresAguardamEntrega.Text = resumo.TotalComputadoresAguardamEntrega.ToString();
        KpiEquipamentoObsoleto.Text = resumo.TotalEquipamentoObsoleto.ToString();

        // Distribuição do Parque de Equipamento (gauges: % de cada tipo face ao total geral)
        var totalEquip = resumo.TotalEquipamentoGeral;
        TxtTotalEquipamentoGeral.Text = $"% de cada tipo face ao total de equipamento registado ({totalEquip} equipamentos)";

        GaugeTotalComputadores.Series = ConstruirGaugePercentagem(resumo.TotalComputadores, totalEquip, "#3B82F6");
        TxtGaugeTotalComputadores.Text = $"{resumo.TotalComputadores} / {totalEquip}";

        GaugeComputadoresSecretaria.Series = ConstruirGaugePercentagem(resumo.TotalComputadoresSecretaria, totalEquip, "#2AB7CA");
        TxtGaugeComputadoresSecretaria.Text = $"{resumo.TotalComputadoresSecretaria} / {totalEquip}";

        GaugePortateis.Series = ConstruirGaugePercentagem(resumo.TotalPortateis, totalEquip, "#22C55E");
        TxtGaugePortateis.Text = $"{resumo.TotalPortateis} / {totalEquip}";

        GaugeSwitches.Series = ConstruirGaugePercentagem(resumo.TotalSwitches, totalEquip, "#8B5CF6");
        TxtGaugeSwitches.Text = $"{resumo.TotalSwitches} / {totalEquip}";

        GaugeAccessPoints.Series = ConstruirGaugePercentagem(resumo.TotalAccessPoints, totalEquip, "#F97316");
        TxtGaugeAccessPoints.Text = $"{resumo.TotalAccessPoints} / {totalEquip}";

        GaugeImpressoras.Series = ConstruirGaugePercentagem(resumo.TotalImpressoras, totalEquip, "#EF4444");
        TxtGaugeImpressoras.Text = $"{resumo.TotalImpressoras} / {totalEquip}";

        // 1.1: o valor numérico fica isolado no topo do card (estilo KpiValueStyle, igual aos
        // restantes cards), com o nome da escola/agrupamento como texto secundário por baixo.
        KpiTopEscolaValor.Text = resumo.EscolaMaisIntervencionada is null
            ? "—"
            : resumo.EscolaMaisIntervencionadaTotal.ToString();
        KpiTopEscola.Text = resumo.EscolaMaisIntervencionada ?? "Sem dados";

        KpiTopAgrupamentoValor.Text = resumo.AgrupamentoMaisIntervencionado is null
            ? "—"
            : resumo.AgrupamentoMaisIntervencionadoTotal.ToString();
        KpiTopAgrupamento.Text = resumo.AgrupamentoMaisIntervencionado ?? "Sem dados";

        // Intervenções por mês (cada barra com uma cor diferente para facilitar a leitura)
        ChartPorMes.Series = SeriesColoridasPorBarra(
            resumo.IntervencoesPorMesAnoCorrente.Select(m => m.Total).ToList(),
            resumo.IntervencoesPorMesAnoCorrente.Select(m => m.Mes).ToList());
        ChartPorMes.XAxes = new[]
        {
            new LiveChartsCore.SkiaSharpView.Axis
            {
                Labels = resumo.IntervencoesPorMesAnoCorrente.Select(m => m.Mes).ToArray()
            }
        };

        // Intervenções por categoria (pie)
        ChartPorCategoria.Series = resumo.IntervencoesPorCategoria
            .Where(c => c.Total > 0)
            .Select(c => (ISeries)new PieSeries<int>
            {
                Values = new[] { c.Total },
                Name = c.Categoria,
                Fill = new SolidColorPaint(SKColor.Parse(c.Cor)),
                DataLabelsPaint = PaintValorEmbutido(c.Cor),
                DataLabelsSize = TamanhoValorEmbutido,
                DataLabelsFormatter = point => $"{point.Coordinate.PrimaryValue:0}"
            }).ToArray();

        // Por agrupamento - mês corrente (cada barra com uma cor diferente).
        // Usa-se a Abreviatura (em vez do nome completo) nas séries/eixo para os rótulos não
        // ficarem esmagados; o nome completo de cada uma fica na legenda por baixo do gráfico.
        ChartAgrupamentoMes.Series = SeriesColoridasPorBarra(
            resumo.IntervencoesPorAgrupamentoMesCorrente.Select(a => a.Total).ToList(),
            resumo.IntervencoesPorAgrupamentoMesCorrente.Select(a => a.Abreviatura).ToList());
        ChartAgrupamentoMes.XAxes = new[]
        {
            new LiveChartsCore.SkiaSharpView.Axis
            {
                Labels = resumo.IntervencoesPorAgrupamentoMesCorrente.Select(a => a.Abreviatura).ToArray()
            }
        };
        TxtLegendaAgrupamentoMes.Text = ConstruirLegendaAbreviaturas(resumo.IntervencoesPorAgrupamentoMesCorrente);

        // Por agrupamento - ano corrente (cada barra com uma cor diferente)
        ChartAgrupamentoAno.Series = SeriesColoridasPorBarra(
            resumo.IntervencoesPorAgrupamentoAnoCorrente.Select(a => a.Total).ToList(),
            resumo.IntervencoesPorAgrupamentoAnoCorrente.Select(a => a.Abreviatura).ToList());
        ChartAgrupamentoAno.XAxes = new[]
        {
            new LiveChartsCore.SkiaSharpView.Axis
            {
                Labels = resumo.IntervencoesPorAgrupamentoAnoCorrente.Select(a => a.Abreviatura).ToArray()
            }
        };
        TxtLegendaAgrupamentoAno.Text = ConstruirLegendaAbreviaturas(resumo.IntervencoesPorAgrupamentoAnoCorrente);

        // (2.3) Intervenções por categoria — mês corrente, em gráfico "teia de aranha" (radar).
        // Usa-se sempre todas as categorias (mesmo com total 0) para o polígono ficar completo.
        var categoriasParaRadar = resumo.IntervencoesPorCategoriaMesCorrente;
        ChartPorCategoriaMes.Series = new ISeries[]
        {
            new PolarLineSeries<double>
            {
                Values = categoriasParaRadar.Select(c => (double)c.Total).ToArray(),
                Name = "Intervenções (mês corrente)",
                Fill = new SolidColorPaint(SKColor.Parse("#3B82F6").WithAlpha(70)),
                Stroke = new SolidColorPaint(SKColor.Parse("#3B82F6")) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(SKColor.Parse("#3B82F6")),
                GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1 },
                GeometrySize = 8,
                DataLabelsPaint = PaintValorEmbutido("#3B82F6"),
                DataLabelsSize = TamanhoValorEmbutido,
                DataLabelsFormatter = point => point.Coordinate.PrimaryValue > 0 ? $"{point.Coordinate.PrimaryValue:0}" : ""
            }
        };
        ChartPorCategoriaMes.AngleAxes = new[]
        {
            new LiveChartsCore.SkiaSharpView.PolarAxis { Labels = categoriasParaRadar.Select(c => c.Categoria).ToArray() }
        };
        ChartPorCategoriaMes.RadiusAxes = new[]
        {
            new LiveChartsCore.SkiaSharpView.PolarAxis { MinLimit = 0 }
        };

        // (2.5) Intervenções por categoria, por agrupamento — total anual (barras empilhadas)
        ChartCategoriaAgrupamentoAno.Series = resumo.IntervencoesPorCategoriaEAgrupamentoAno
            .Select(c => (ISeries)new StackedColumnSeries<double>
            {
                Values = c.Totais.Select(v => (double)v).ToArray(),
                Name = c.Categoria,
                Fill = new SolidColorPaint(SKColor.Parse(c.Cor)),
                DataLabelsPaint = PaintValorEmbutido(c.Cor),
                DataLabelsSize = 11,
                DataLabelsFormatter = point => point.Coordinate.PrimaryValue > 0 ? $"{point.Coordinate.PrimaryValue:0}" : ""
            }).ToArray();
        ChartCategoriaAgrupamentoAno.XAxes = new[]
        {
            new LiveChartsCore.SkiaSharpView.Axis { Labels = resumo.AgrupamentosAbreviaturasAno.ToArray() }
        };
        TxtLegendaCategoriaAgrupamentoAno.Text = resumo.LegendaAgrupamentosAno;

        // (2.5) Intervenções por categoria, por agrupamento — mês corrente (barras empilhadas)
        ChartCategoriaAgrupamentoMes.Series = resumo.IntervencoesPorCategoriaEAgrupamentoMes
            .Select(c => (ISeries)new StackedColumnSeries<double>
            {
                Values = c.Totais.Select(v => (double)v).ToArray(),
                Name = c.Categoria,
                Fill = new SolidColorPaint(SKColor.Parse(c.Cor)),
                DataLabelsPaint = PaintValorEmbutido(c.Cor),
                DataLabelsSize = 11,
                DataLabelsFormatter = point => point.Coordinate.PrimaryValue > 0 ? $"{point.Coordinate.PrimaryValue:0}" : ""
            }).ToArray();
        ChartCategoriaAgrupamentoMes.XAxes = new[]
        {
            new LiveChartsCore.SkiaSharpView.Axis { Labels = resumo.AgrupamentosAbreviaturasMes.ToArray() }
        };
        TxtLegendaCategoriaAgrupamentoMes.Text = resumo.LegendaAgrupamentosMes;

        // Pendentes — Escola vs. DISIA (gráfico de anel / donut)
        // Séries com valor 0 são ocultadas para não deixar uma fatia vazia com etiqueta "0".
        var seriesPendentes = new List<ISeries>();
        if (resumo.PendentesEscola > 0)
            seriesPendentes.Add(new PieSeries<int>
            {
                Values = new[] { resumo.PendentesEscola },
                Name = "Pendentes na Escola",
                Fill = new SolidColorPaint(SKColor.Parse("#EF4444")),
                InnerRadius = 60,
                DataLabelsPaint = PaintValorEmbutido("#EF4444"),
                DataLabelsSize = TamanhoValorEmbutido,
                DataLabelsFormatter = point => $"{point.Coordinate.PrimaryValue:0}"
            });
        if (resumo.PendentesDisia > 0)
            seriesPendentes.Add(new PieSeries<int>
            {
                Values = new[] { resumo.PendentesDisia },
                Name = "Pendentes na DISIA (equipamento)",
                Fill = new SolidColorPaint(SKColor.Parse("#7C3AED")),
                InnerRadius = 60,
                DataLabelsPaint = PaintValorEmbutido("#7C3AED"),
                DataLabelsSize = TamanhoValorEmbutido,
                DataLabelsFormatter = point => $"{point.Coordinate.PrimaryValue:0}"
            });
        if (resumo.PendentesAtividadesDisia > 0)
            seriesPendentes.Add(new PieSeries<int>
            {
                Values = new[] { resumo.PendentesAtividadesDisia },
                Name = "Pendentes na DISIA (atividades)",
                Fill = new SolidColorPaint(SKColor.Parse("#F59E0B")),
                InnerRadius = 60,
                DataLabelsPaint = PaintValorEmbutido("#F59E0B"),
                DataLabelsSize = TamanhoValorEmbutido,
                DataLabelsFormatter = point => $"{point.Coordinate.PrimaryValue:0}"
            });
        ChartPendentes.Series = seriesPendentes.ToArray();

        UltimoResumo = resumo;
    }
}
