using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class PedidosWindow : Window
{
    private List<PedidoIntervencao> _todos = new();

    /// <summary>Lista atualmente visível na grelha (já com a pesquisa aplicada) — usada pelo
    /// "Relatório do Módulo" para o relatório refletir exatamente o que está a ser visto.</summary>
    private List<PedidoIntervencao> _visiveis = new();

    public PedidosWindow()
    {
        InitializeComponent();
        // Perfil Guest (Services/SessaoAtual.PodeEditar): acesso só de leitura a este módulo -
        // ver Services/PermissoesService.cs.
        LeiriaDISIA.Services.PermissoesService.AplicarSomenteLeituraSeGuest(BtnInserir);

        // (7.1) Legenda dos quadrados de cor da coluna "Estado"
        LegendaEstados.ItemsSource = Enum.GetValues<EstadoPedido>()
            .Select(estado =>
            {
                var nomeExibicao = App.Db.EstadosCorPersonalizados
                    .FirstOrDefault(e => e.Grupo == GruposEstadoCor.Pedido && e.NomeEstado == estado.ToString())
                    ?.NomeExibicao;
                return new { Nome = string.IsNullOrWhiteSpace(nomeExibicao) ? estado.ToString() : nomeExibicao, Cor = EstadoCores.CorEstadoPedido(estado) };
            })
            .ToList();

        Recarregar();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Filtro_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void Recarregar()
    {
        _todos = App.Db.PedidosIntervencao
            .Include(p => p.Escola)
            .Include(p => p.Agrupamento)
            .OrderByDescending(p => p.DataPedido)
            .ToList();
        AtualizarCartoesResumo();
        AplicarFiltro();
    }

    /// <summary>Cartões de resumo do módulo (ver Views/PedidosWindow.xaml) — sempre calculados
    /// sobre TODOS os pedidos (<see cref="_todos"/>), não sobre a lista filtrada/pesquisada visível
    /// na grelha, já que são estatísticas do módulo como um todo. As médias de tempo usam
    /// <see cref="PedidoIntervencao.DiasParaConclusaoExcluindoEspera"/> — que já exclui o tempo
    /// passado "Em Espera" (ver Models/PedidoIntervencao.cs) — e só entram pedidos concluídos DEPOIS
    /// de este rastreio ter sido adicionado; pedidos antigos, já concluídos antes disso, continuam a
    /// contar para a média (não há forma de saber retroativamente quanto tempo estiveram "Em
    /// Espera"), mas o próprio "vamos começar a contabilizar isto agora" já foi assumido ao pedir
    /// esta funcionalidade.
    ///
    /// "Ano" e "Mês" agrupam por datas DIFERENTES, de propósito — "Ano" agrupa pela data de
    /// CONCLUSÃO (todos os pedidos fechados este ano, venham de quando vierem), mas "Mês" agrupa
    /// pela data do PEDIDO (só pedidos feitos este mês) — um pedido feito em julho e só concluído
    /// agora em setembro conta para a média anual (correto, mostra o tempo real que levou), mas NÃO
    /// para a média mensal, que ficaria "contaminada" pela sua duração toda mesmo só tendo sido
    /// concluído por coincidência este mês.</summary>
    private void AtualizarCartoesResumo()
    {
        TxtCardEmAberto.Text = _todos.Count(p => p.EstaEmAberto).ToString();
        // "Em Espera" fica de fora aqui de propósito — um pedido "Em Espera" há muito tempo não é
        // um pedido que a DISIA esteja a deixar por fazer, é um pedido parado à espera de algo
        // (ex.: aquisição de material) que não depende da DISIA; contá-lo como "urgente" ao lado de
        // pedidos genuinamente parados por fazer (Pendente/Em Andamento) distorcia o indicador.
        TxtCardUrgentes.Text = _todos.Count(p =>
            (p.Estado is EstadoPedido.Pendente or EstadoPedido.EmAndamento) && p.DiasEmAberto > 21).ToString();

        var hoje = DateTime.Today;

        var concluidosEsteAno = _todos.Where(p => p.Estado == EstadoPedido.Concluido && p.DataConclusao?.Year == hoje.Year).ToList();
        TxtCardConcluidosAno.Text = concluidosEsteAno.Count.ToString();
        TxtCardMediaAno.Text = FormatarMediaDias(concluidosEsteAno);

        var pedidosDesteMes = _todos.Where(p =>
            p.Estado == EstadoPedido.Concluido && p.DataPedido.Year == hoje.Year && p.DataPedido.Month == hoje.Month);
        TxtCardMediaMes.Text = FormatarMediaDias(pedidosDesteMes);

        // Cartão de comparação (a pedido) — mesmo cálculo do mês corrente (agrupa por DATA DO
        // PEDIDO, não de conclusão — ver comentário acima), mas para o mês imediatamente anterior.
        var mesAnterior = hoje.AddMonths(-1);
        var pedidosMesAnterior = _todos.Where(p =>
            p.Estado == EstadoPedido.Concluido && p.DataPedido.Year == mesAnterior.Year && p.DataPedido.Month == mesAnterior.Month);
        TxtCardMediaMesAnterior.Text = FormatarMediaDias(pedidosMesAnterior);
    }

    /// <summary>"—" sem nenhum pedido concluído no período (nada para fazer média), senão a média
    /// arredondada a uma casa decimal, em dias.</summary>
    private static string FormatarMediaDias(IEnumerable<PedidoIntervencao> pedidos)
    {
        var dias = pedidos.Select(p => p.DiasParaConclusaoExcluindoEspera).Where(d => d != null).Select(d => d!.Value).ToList();
        return dias.Count == 0 ? "—" : $"{dias.Average():0.#} dias";
    }

    private void AplicarFiltro()
    {
        IEnumerable<PedidoIntervencao> resultado = _todos;

        // Por omissão, só mostra os pedidos que ainda precisam de atenção (EstaEmAberto: Pendente,
        // Em Andamento, Em Espera) — os já Concluídos/Cancelados deixam de aparecer assim que
        // mudam para esse estado, para não se acumularem na lista e dificultarem ver o que
        // realmente falta resolver. O checkbox "Mostrar concluídos/cancelados" reverte isto,
        // mostrando tudo (mesmo critério "EstaEmAberto" já usado noutros sítios da aplicação,
        // ex.: contagem "Em Aberto" nos relatórios de Pedidos).
        if (ChkMostrarConcluidos?.IsChecked != true)
            resultado = resultado.Where(p => p.EstaEmAberto);

        var termo = TxtPesquisa?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            resultado = resultado.Where(p =>
                (p.Escola != null && p.Escola.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (p.Agrupamento != null && p.Agrupamento.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (p.Razao != null && p.Razao.Contains(termo, StringComparison.OrdinalIgnoreCase)));
        }

        Grid.ItemsSource = _visiveis = resultado.ToList();
    }

    private void ChkMostrarConcluidos_Changed(object sender, RoutedEventArgs e) => AplicarFiltro();

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new PedidoEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not PedidoIntervencao pedido) return;

        var janela = new PedidoEditWindow(pedido) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void PlanearRota_Click(object sender, RoutedEventArgs e)
    {
        var janela = new PlanearRotaWindow { Owner = this };
        janela.ShowDialog();
        Recarregar();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        // (1.1) O relatório do módulo reflete exatamente o que está a ser visto na grelha (pesquisa
        // aplicada), em vez da lista completa de pedidos.
        if (_visiveis.Count == 0)
        {
            MessageBox.Show("Não existem pedidos a corresponder ao filtro atual para incluir no relatório.",
                "Sem dados para o relatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de pedidos de intervenção",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Pedidos_Intervencao_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaPedidosIntervencao(dialog.FileName, _visiveis.Select(p => p.Id).ToList());

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
