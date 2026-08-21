using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

public partial class PedidosView : UserControl
{
    private PedidoIntervencao? _selecionado;

    public PedidosView()
    {
        InitializeComponent();
        CmbEstado.ItemsSource = Enum.GetValues<EstadoPedido>();
        CmbEscola.ItemsSource = App.Db.Escolas.Include(e => e.Agrupamento).OrderBy(e => e.Nome).ToList();
        Recarregar();
        Novo_Click(this, new RoutedEventArgs());
    }

    private void Recarregar()
    {
        Grid.ItemsSource = App.Db.PedidosIntervencao
            .Include(p => p.Escola)
            .Include(p => p.Agrupamento)
            .OrderByDescending(p => p.DataPedido)
            .ToList();
    }

    private void CmbEscola_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbEscola.SelectedItem is Escola escola)
            TxtDadosEscola.Text = $"{escola.Agrupamento?.Nome}  •  Freguesia: {escola.Freguesia}  •  Cód. GEPE: {escola.CodGEPE}";
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionado = Grid.SelectedItem as PedidoIntervencao;
        if (_selecionado == null) return;

        DpData.SelectedDate = _selecionado.DataPedido;
        CmbEscola.SelectedItem = ((List<Escola>)CmbEscola.ItemsSource).FirstOrDefault(e => e.Id == _selecionado.EscolaId);
        TxtSolicitante.Text = _selecionado.Solicitante;
        TxtContacto.Text = _selecionado.ContactoSolicitante;
        TxtRazao.Text = _selecionado.Razao;
        CmbEstado.SelectedItem = _selecionado.Estado;
        TxtMotivoPendente.Text = _selecionado.MotivoPendente;
    }

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        _selecionado = null;
        Grid.SelectedItem = null;
        DpData.SelectedDate = DateTime.Today;
        CmbEscola.SelectedItem = null;
        TxtSolicitante.Clear();
        TxtContacto.Clear();
        TxtRazao.Clear();
        CmbEstado.SelectedItem = EstadoPedido.Pendente;
        TxtMotivoPendente.Clear();
        TxtDadosEscola.Text = "";
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (CmbEscola.SelectedItem is not Escola escola || string.IsNullOrWhiteSpace(TxtRazao.Text))
        {
            MessageBox.Show("Selecione a escola e indique a razão do pedido.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var estado = (EstadoPedido)(CmbEstado.SelectedItem ?? EstadoPedido.Pendente);

        if (_selecionado == null)
        {
            _selecionado = new PedidoIntervencao();
            App.Db.PedidosIntervencao.Add(_selecionado);
        }

        _selecionado.DataPedido = DpData.SelectedDate ?? DateTime.Today;
        _selecionado.EscolaId = escola.Id;
        _selecionado.AgrupamentoId = escola.AgrupamentoId;
        _selecionado.Solicitante = TxtSolicitante.Text;
        _selecionado.ContactoSolicitante = TxtContacto.Text;
        _selecionado.Razao = TxtRazao.Text.Trim();
        _selecionado.Estado = estado;
        _selecionado.MotivoPendente = estado == EstadoPedido.Pendente ? TxtMotivoPendente.Text : null;
        if (estado == EstadoPedido.Concluido && _selecionado.DataConclusao == null)
            _selecionado.DataConclusao = DateTime.Today;

        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionado == null) return;
        if (MessageBox.Show("Eliminar este pedido de intervenção?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        App.Db.PedidosIntervencao.Remove(_selecionado);
        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }

    private void CriarIntervencao_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionado == null)
        {
            MessageBox.Show("Guarde primeiro o pedido antes de criar a intervenção associada.",
                "Ação necessária", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var escola = App.Db.Escolas.Include(x => x.Agrupamento).First(x => x.Id == _selecionado.EscolaId);
        var pedidoAtual = App.Db.PedidosIntervencao.First(p => p.Id == _selecionado.Id);

        var dialog = new IntervencaoQuickWindow(escola, pedidoAtual) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();

        if (dialog.Sucesso)
        {
            Recarregar();
            Novo_Click(sender, e);
            MessageBox.Show("Intervenção registada e pedido marcado como concluído.", "Sucesso",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
