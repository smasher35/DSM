using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.SKCharts;
using SkiaSharp;

namespace LeiriaDISIA.Services;

/// <summary>
/// Captura o Dashboard (KPIs, gráficos e gauges — ver <see cref="LeiriaDISIA.Views.DashboardView"/>)
/// como uma imagem PNG, para poder ser incluído automaticamente no Relatório Mensal de Atividades,
/// logo a seguir ao gráfico "Intervenções por Mês (Ano Corrente)" — dando aí uma panorâmica geral
/// de todo o ecossistema (equipamento, intervenções, atividades), sem depender de nenhum screenshot
/// anexado manualmente.
/// </summary>
public static class DashboardSnapshotService
{
    /// <summary>Renderiza uma nova instância do Dashboard (com os dados mais recentes da aplicação)
    /// para PNG. Devolve null se a captura falhar por qualquer motivo — o relatório continua a ser
    /// gerado na mesma, apenas sem esta imagem.
    ///
    /// O Dashboard usa sempre a disposição UHD (ver <see cref="DashboardResolucaoService"/>), que
    /// foi desenhada para uma zona de conteúdo até 2000px de largura (8 cartões KPI por linha, 6
    /// gauges numa só linha) — por isso a captura usa sempre 2560px de largura por omissão.
    /// Passar explicitamente "largura" continua a substituir esta escolha automática, para quem
    /// chamar o método diretamente com um valor específico.</summary>
    public static byte[]? Capturar(int? largura = null)
    {
        Window? janelaAuxiliar = null;
        try
        {
            var dashboard = PrepararDashboardParaCaptura(largura, out janelaAuxiliar);

            var alturaReal = (int)Math.Ceiling(dashboard.ActualHeight);
            var larguraReal = (int)Math.Ceiling(dashboard.ActualWidth);
            if (larguraReal <= 0 || alturaReal <= 0) return null;

            var bitmap = new RenderTargetBitmap(larguraReal, alturaReal, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(dashboard);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
        finally
        {
            janelaAuxiliar?.Close();
        }
    }

    /// <summary>Captura, em separado, cada uma das secções de gráficos do Dashboard (gauges do
    /// parque de equipamento, intervenções por mês, intervenções por categoria e intervenções por
    /// agrupamento) — ver <see cref="LeiriaDISIA.Views.DashboardView.CapturarSeccoes"/>. Usado pelo
    /// Relatório Mensal para poder organizar estas secções em linhas compactas (2 gráficos lado a
    /// lado) e forçar a secção "Intervenções por Agrupamento" para uma página própria, em vez de
    /// depender de uma única imagem gigante dividida às cegas em faixas verticais como
    /// <see cref="Capturar"/> fazia antes (esse método e <see cref="DividirEmFaixasVerticais"/>
    /// deixaram de ser usados pelo Relatório Mensal, mas ficam disponíveis para qualquer outra
    /// necessidade futura de capturar o Dashboard inteiro numa única imagem). Devolve null se a
    /// captura falhar por qualquer motivo — o relatório continua a ser gerado na mesma, apenas sem
    /// esta secção.</summary>
    public static LeiriaDISIA.Views.DashboardView.DashboardSeccoesSnapshot? CapturarSeccoes(int? largura = null)
    {
        Window? janelaAuxiliar = null;
        try
        {
            var dashboard = PrepararDashboardParaCaptura(largura, out janelaAuxiliar);
            return dashboard.CapturarSeccoes();
        }
        catch
        {
            return null;
        }
        finally
        {
            janelaAuxiliar?.Close();
        }
    }

    /// <summary>Desenha o gráfico de barras "Intervenções por Agrupamento" (ano ou mês corrente)
    /// diretamente com o motor de desenho do LiveChartsCore, em memória — sem precisar de nenhum
    /// controlo WPF já visível no ecrã, nem de janelas auxiliares fora da área visível, nem de
    /// esperar por ciclos de "Render" do WPF.
    ///
    /// A versão anterior (uma captura de ecrã do <c>CartesianChart</c> já desenhado no Dashboard,
    /// tal como as restantes secções da Vista Geral) revelou-se pouco fiável precisamente para este
    /// gráfico: o número de barras varia todos os meses (consoante quantos agrupamentos têm
    /// intervenções), e o redesenho do SkiaSharp/LiveCharts nesse controlo é "throttled" — agrupa
    /// várias atualizações seguidas num único redesenho, com um pequeno atraso interno que não
    /// depende de nenhum ciclo de mensagens do WPF — nem sempre a tempo da captura, resultando por
    /// vezes num gráfico vazio ou só com algumas barras.
    ///
    /// Para este cenário exato (gerar uma imagem de um gráfico "no vazio", sem nenhuma UI), o
    /// LiveChartsCore disponibiliza a classe <c>SKCartesianChart</c>: monta-se o gráfico
    /// atribuindo-lhe diretamente as séries/eixos (reaproveitando
    /// <see cref="LeiriaDISIA.Views.DashboardView.SeriesColoridasPorBarra"/>, exatamente a mesma
    /// lógica de cores usada no ecrã), e <c>SaveImage(...)</c> desenha-o de forma imediata e síncrona — sem depender
    /// de nenhum ciclo de render do WPF, logo sem esta classe de problema. Devolve <c>null</c> se não
    /// houver dados ou a operação falhar por qualquer motivo — o relatório continua a ser gerado na
    /// mesma, apenas sem esta imagem.</summary>
    internal static byte[]? RenderizarGraficoAgrupamento(
        IReadOnlyList<(string Agrupamento, string Abreviatura, int Total)> dados, int largura = 900, int altura = 480)
    {
        if (dados.Count == 0) return null;

        var caminhoTemp = Path.Combine(Path.GetTempPath(), $"LeiriaDISIA_grafico_{Guid.NewGuid():N}.png");
        try
        {
            var chart = new SKCartesianChart
            {
                Width = largura,
                Height = altura,
                Background = SKColors.White,
                LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden,
                Series = LeiriaDISIA.Views.DashboardView.SeriesColoridasPorBarra(
                    dados.Select(a => a.Total).ToList(),
                    dados.Select(a => a.Abreviatura).ToList()),
                XAxes = new[] { new Axis { Labels = dados.Select(a => a.Abreviatura).ToArray() } }
            };

            chart.SaveImage(caminhoTemp);
            return File.ReadAllBytes(caminhoTemp);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { File.Delete(caminhoTemp); } catch { /* ficheiro temporário — falha a apagar não é crítica */ }
        }
    }

    /// <summary>Cria a janela auxiliar de captura (fora da área visível do ecrã, sem aparecer na
    /// barra de tarefas) com uma nova instância do Dashboard (dados mais recentes, animações
    /// desligadas), aguarda o layout e os ciclos de render necessários para o LiveCharts/SkiaSharp
    /// terminarem de desenhar, e devolve o Dashboard já pronto a ser capturado por
    /// <see cref="Capturar"/> ou <see cref="CapturarSeccoes"/>. A janela criada é devolvida em
    /// <paramref name="janelaAuxiliar"/> para o chamador a poder fechar no seu próprio "finally",
    /// mesmo que a captura em si falhe a meio.</summary>
    private static LeiriaDISIA.Views.DashboardView PrepararDashboardParaCaptura(int? largura, out Window janelaAuxiliar)
    {
        var larguraCaptura = largura ?? (DashboardResolucaoService.UhdAtivo ? 2560 : 1400);

        var dashboard = new LeiriaDISIA.Views.DashboardView(desativarAnimacoes: true);

        // O LiveCharts (assente em SkiaSharp) só desenha quando o controlo está efetivamente
        // ligado a uma janela real (é preciso um "PresentationSource" válido) — por isso usa-se
        // uma janela auxiliar, colocada bem fora da área visível do ecrã e sem aparecer na
        // barra de tarefas, em vez de tentar desenhar o UserControl "solto".
        janelaAuxiliar = new Window
        {
            Content = dashboard,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Left = -100000,
            Top = -100000,
            Width = larguraCaptura,
            SizeToContent = SizeToContent.Height
        };

        janelaAuxiliar.Show();

        dashboard.UpdateLayout();

        // O desenho do SkiaSharp acontece de forma assíncrona, ligado ao ciclo de render do
        // WPF — não fica pronto logo a seguir ao layout. Isto força a thread da UI a aguardar
        // por vários ciclos de "Render" completos antes da captura. Agora que o DashboardView
        // recebe "desativarAnimacoes: true" e realmente desliga a animação de todos os gráficos
        // (ver DashboardView.xaml.cs), cada gráfico já nasce no seu estado final — não há uma
        // animação de ~1,5s a decorrer — por isso alguns ciclos de Render chegam com folga para
        // todos os gráficos (gauges E barras/pizza/radar) ficarem completos antes da captura.
        for (var i = 0; i < 5; i++)
            EsperarProximoRender();

        // Os gráficos LiveChartsCore/SkiaSharp (sobretudo os de barras "Intervenções por
        // Agrupamento", cujas séries mudam de tamanho consoante o número de agrupamentos com
        // dados nesse mês/ano) não redesenham de imediato quando as Series/eixos são atribuídos —
        // internamente agrupam ("throttle") várias atualizações seguidas num único redesenho, com
        // um pequeno atraso interno que não depende do ciclo de Render do WPF. Esperar apenas por
        // ciclos de Render (acima) garante que o LAYOUT do WPF está pronto, mas não garante que
        // esse redesenho "throttled" já aconteceu — em máquinas mais lentas, ou quando há mais
        // agrupamentos com dados (mais barras/eixo maior a recalcular), a captura podia ocorrer
        // mesmo antes disso, dando um gráfico vazio ou só com parte das barras. Este atraso extra,
        // em tempo real (não apenas em ciclos de mensagens), dá-lhe folga para terminar sempre,
        // seguido de mais alguns ciclos de Render para apanhar esse redesenho já concluído.
        EsperarTempoReal(TimeSpan.FromMilliseconds(400));
        for (var i = 0; i < 3; i++)
            EsperarProximoRender();

        return dashboard;
    }

    /// <summary>Bloqueia a thread da UI, através de um <see cref="DispatcherFrame"/>, até ao
    /// próximo ciclo de "Render" (e mais um de "ApplicationIdle" a seguir, por segurança), dando
    /// tempo ao SkiaSharp/LiveCharts para desenhar o dashboard antes de o capturarmos.</summary>
    private static void EsperarProximoRender()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false))));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Bloqueia a thread da UI durante <paramref name="duracao"/> de tempo REAL (relógio),
    /// continuando a processar a fila de mensagens (via <see cref="DispatcherFrame"/>) durante essa
    /// espera — ao contrário de <see cref="EsperarProximoRender"/>, que apenas avança um ciclo de
    /// mensagens de cada vez (o que pode terminar em frações de milissegundo, sem tempo real
    /// nenhum ter decorrido). Existe especificamente para dar folga a atualizações "throttled" do
    /// LiveChartsCore, que têm um pequeno atraso interno medido em tempo real, não em número de
    /// ciclos de render do WPF.</summary>
    private static void EsperarTempoReal(TimeSpan duracao)
    {
        var frame = new DispatcherFrame();
        var temporizador = new DispatcherTimer(DispatcherPriority.Background) { Interval = duracao };
        temporizador.Tick += (_, _) =>
        {
            temporizador.Stop();
            frame.Continue = false;
        };
        temporizador.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Divide a imagem devolvida por <see cref="Capturar"/> em várias faixas verticais
    /// lado a lado — usado pelo relatório mensal para manter o Dashboard sempre em página A4
    /// retrato (nunca em paisagem), em vez de espremer a imagem inteira, naturalmente larga
    /// (sobretudo na disposição UHD), numa única página estreita. Cada faixa fica com uma fração
    /// da largura original mas a MESMA altura; empilhadas depois verticalmente no relatório, cada
    /// uma só precisa de um fator de redução bem menor para caber na largura útil da página — o
    /// texto e os gráficos ficam por isso maiores e mais legíveis do que encolher a imagem inteira
    /// de uma só vez.
    ///
    /// Devolve sempre pelo menos uma faixa: a imagem original, sem dividir, se ela já não for
    /// muito mais larga do que alta, ou se a divisão falhar por qualquer motivo (o relatório
    /// continua a ser gerado na mesma).</summary>
    public static IReadOnlyList<byte[]> DividirEmFaixasVerticais(byte[] pngOriginal, int maxFaixas = 3)
    {
        try
        {
            using var streamOrigem = new MemoryStream(pngOriginal);
            var decoder = BitmapDecoder.Create(streamOrigem, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var largura = frame.PixelWidth;
            var altura = frame.PixelHeight;
            if (largura <= 0 || altura <= 0) return new[] { pngOriginal };

            // Só vale a pena dividir quando a imagem é claramente mais larga do que alta; senão a
            // imagem original já cabe razoavelmente bem numa página A4 retrato tal como está.
            const double aspetoAlvoPorFaixa = 1.15;
            var numFaixas = Math.Clamp((int)Math.Ceiling(largura / (altura * aspetoAlvoPorFaixa)), 1, maxFaixas);
            if (numFaixas <= 1) return new[] { pngOriginal };

            var larguraFaixa = largura / numFaixas;
            var faixas = new List<byte[]>(numFaixas);

            for (var i = 0; i < numFaixas; i++)
            {
                var x = i * larguraFaixa;
                var larguraEsta = i == numFaixas - 1 ? largura - x : larguraFaixa; // a última faixa absorve o resto
                var recorte = new CroppedBitmap(frame, new Int32Rect(x, 0, larguraEsta, altura));

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(recorte));
                using var streamDestino = new MemoryStream();
                encoder.Save(streamDestino);
                faixas.Add(streamDestino.ToArray());
            }

            return faixas;
        }
        catch
        {
            // Qualquer falha a dividir a imagem: devolve a imagem original inteira, sem dividir —
            // o relatório continua a ser gerado na mesma.
            return new[] { pngOriginal };
        }
    }
}
