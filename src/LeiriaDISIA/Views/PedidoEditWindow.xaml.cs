using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class PedidoEditWindow : Window
{
    private readonly PedidoIntervencao? _existente;
    private List<Escola> _todasEscolas = new();
    public bool Sucesso { get; private set; }

    public PedidoEditWindow(PedidoIntervencao? pedido)
    {
        InitializeComponent();

        // Perfil Guest (Services/SessaoAtual.PodeEditar): não pode criar/editar/eliminar
        // registos - fecha-se logo a seguir a abrir, com um aviso, em vez de deixar o
        // formulário aberto só para descobrir mais tarde que não consegue gravar nada.
        if (LeiriaDISIA.Services.PermissoesService.BloquearAberturaSeGuest(this)) return;
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _existente = pedido;

        _todasEscolas = App.Db.Escolas.Include(e => e.Agrupamento).Where(e => e.Estado != EstadosEscola.Desativada).OrderBy(e => e.Nome).ToList();
        CmbEscola.ItemsSource = _todasEscolas;
        CmbEstado.ItemsSource = Enum.GetValues<EstadoPedido>();

        if (pedido == null)
        {
            TxtTitulo.Text = "Novo Pedido de Intervenção";
            DpData.SelectedDate = DateTime.Today;
            CmbEstado.SelectedItem = EstadoPedido.EmAndamento;
            PainelTempoAberto.Visibility = Visibility.Collapsed;
            return;
        }

        TxtTitulo.Text = "Editar Pedido de Intervenção";
        DpData.SelectedDate = pedido.DataPedido;
        CmbEscola.SelectedItem = _todasEscolas.FirstOrDefault(e => e.Id == pedido.EscolaId);
        TxtNumeroSuporteSiga.Text = pedido.NumeroSuporteSiga;
        TxtSolicitante.Text = pedido.Solicitante;
        TxtContacto.Text = pedido.ContactoSolicitante;
        TxtRazao.Text = pedido.Razao;
        CmbEstado.SelectedItem = pedido.Estado;
        TxtMotivoPendente.Text = pedido.MotivoPendente;
        TxtDuracaoEstimada.Text = pedido.DuracaoEstimadaMinutos?.ToString();
        ChkObrigatorioNaRota.IsChecked = pedido.ObrigatorioNaRota;

        AtualizarPainelTempoAberto(pedido);
    }

    /// <summary>(5.1) Filtra a lista de escolas do ComboBox à medida que se escreve na caixa de
    /// pesquisa por cima, para ser mais rápido encontrar a escola pretendida numa lista longa.
    /// Mantém a escola já selecionada, se ainda corresponder ao filtro.</summary>
    private void TxtPesquisaEscola_TextChanged(object sender, TextChangedEventArgs e)
    {
        var selecionadaAtual = CmbEscola.SelectedItem as Escola;
        var termo = TxtPesquisaEscola.Text?.Trim();

        var filtradas = string.IsNullOrWhiteSpace(termo)
            ? _todasEscolas
            : _todasEscolas.Where(esc =>
                (esc.Nome != null && esc.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (esc.Freguesia != null && esc.Freguesia.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (esc.CodGEPE != null && esc.CodGEPE.Value.ToString().Contains(termo, StringComparison.OrdinalIgnoreCase)))
              .ToList();

        CmbEscola.ItemsSource = filtradas;
        CmbEscola.SelectedItem = selecionadaAtual != null && filtradas.Contains(selecionadaAtual)
            ? selecionadaAtual
            : null;
    }

    private void AtualizarPainelTempoAberto(PedidoIntervencao pedido)
    {
        if (!pedido.EstaEmAberto)
        {
            PainelTempoAberto.Visibility = Visibility.Collapsed;
            return;
        }

        PainelTempoAberto.Visibility = Visibility.Visible;
        var dias = pedido.DiasEmAberto;
        TxtTempoAberto.Text = $"Este pedido está em aberto há {dias} dia(s).";
        BolaTempoAberto.Fill = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(pedido.CorTempoEmAberto));
    }

    private void CmbEscola_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbEscola.SelectedItem is Escola escola)
            TxtDadosEscola.Text = $"{escola.Agrupamento?.Nome}  •  Freguesia: {escola.Freguesia}  •  Cód. GEPE: {escola.CodGEPE}";
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (CmbEscola.SelectedItem is not Escola escola || string.IsNullOrWhiteSpace(TxtRazao.Text))
        {
            MessageBox.Show("Selecione a escola e indique a razão do pedido.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DpData.SelectedDate == null)
        {
            MessageBox.Show("Indique a data do pedido.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int? duracaoEstimada = null;
        var textoDuracao = TxtDuracaoEstimada.Text?.Trim();
        if (!string.IsNullOrEmpty(textoDuracao))
        {
            if (!int.TryParse(textoDuracao, out var valor) || valor <= 0)
            {
                MessageBox.Show(
                    "A duração estimada da intervenção deve ser um número inteiro de minutos maior do que zero " +
                    "(ou deixe em branco para usar a duração padrão).",
                    "Dados inválidos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            duracaoEstimada = valor;
        }

        var estado = (EstadoPedido)(CmbEstado.SelectedItem ?? EstadoPedido.Pendente);

        PedidoIntervencao pedido;
        if (_existente == null)
        {
            pedido = new PedidoIntervencao();
            App.Db.PedidosIntervencao.Add(pedido);
        }
        else
        {
            pedido = App.Db.PedidosIntervencao.First(p => p.Id == _existente.Id);
        }

        pedido.DataPedido = DpData.SelectedDate ?? DateTime.Today;
        pedido.EscolaId = escola.Id;
        pedido.AgrupamentoId = escola.AgrupamentoId;
        pedido.NumeroSuporteSiga = string.IsNullOrWhiteSpace(TxtNumeroSuporteSiga.Text) ? null : TxtNumeroSuporteSiga.Text.Trim();
        pedido.Solicitante = TxtSolicitante.Text;
        pedido.ContactoSolicitante = TxtContacto.Text;
        pedido.Razao = TxtRazao.Text.Trim();
        pedido.Estado = estado;
        pedido.MotivoPendente = estado is EstadoPedido.Pendente or EstadoPedido.EmEspera ? TxtMotivoPendente.Text : null;
        pedido.DuracaoEstimadaMinutos = duracaoEstimada;
        pedido.ObrigatorioNaRota = ChkObrigatorioNaRota.IsChecked == true;
        if (estado == EstadoPedido.Concluido && pedido.DataConclusao == null)
            pedido.DataConclusao = DateTime.Today;

        App.Db.SaveChanges();
        Sucesso = true;
        Close();
    }

    private void CriarIntervencao_Click(object sender, RoutedEventArgs e)
    {
        if (_existente == null)
        {
            MessageBox.Show("Guarde primeiro o pedido antes de criar a intervenção associada.",
                "Ação necessária", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var escola = App.Db.Escolas.Include(x => x.Agrupamento).First(x => x.Id == _existente.EscolaId);
        var pedidoAtual = App.Db.PedidosIntervencao.First(p => p.Id == _existente.Id);

        var dialog = new IntervencaoEditWindow(null, escola, pedidoAtual) { Owner = this };
        dialog.ShowDialog();

        if (dialog.Sucesso)
        {
            Sucesso = true;
            MessageBox.Show("Intervenção registada e pedido marcado como concluído.", "Sucesso",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}
