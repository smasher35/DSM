using System.Windows;
using System.Windows.Input;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

/// <summary>
/// Janela de seleção de um Pedido de Intervenção em aberto, usada a partir de
/// <see cref="IntervencaoEditWindow"/> (botão "🔗 Associar a um Pedido...") para permitir associar
/// uma intervenção registada diretamente (sem passar pelo módulo de Pedidos) a um pedido já
/// existente — para que, ao fechar a intervenção, o pedido seja também automaticamente marcado
/// como concluído (tal como já acontecia quando a intervenção nasce a partir do pedido, em
/// <see cref="PedidoEditWindow"/>).
/// </summary>
public partial class SelecionarPedidoWindow : Window
{
    private readonly List<PedidoIntervencao> _todosOsPedidos;

    /// <summary>Pedido escolhido pelo utilizador; só tem valor quando a janela fecha com
    /// <see cref="Window.DialogResult"/> = true.</summary>
    public PedidoIntervencao? PedidoSelecionado { get; private set; }

    public SelecionarPedidoWindow()
    {
        InitializeComponent();
        // Modo Compacto (Administração → Aparência): em ecrãs pequenos/portáteis, encolhe a
        // janela para caber na área de trabalho disponível - ver Services/JanelaTamanhoHelper.cs.
        // Sem efeito em ecrãs normais/grandes ou com o modo desativado.
        JanelaTamanhoHelper.AjustarSePreciso(this);
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        // Só pedidos em aberto (Pendente/EmAndamento/EmEspera) e ainda sem intervenção associada -
        // um pedido já concluído/cancelado, ou já ligado a outra intervenção, não faz sentido
        // aparecer aqui para se voltar a associar.
        _todosOsPedidos = App.Db.PedidosIntervencao
            .Include(p => p.Escola)
            .Include(p => p.Agrupamento)
            .Where(p => p.IntervencaoId == null &&
                        (p.Estado == EstadoPedido.Pendente || p.Estado == EstadoPedido.EmAndamento || p.Estado == EstadoPedido.EmEspera))
            .OrderByDescending(p => p.DataPedido)
            .ToList();

        Grid.ItemsSource = _todosOsPedidos;
        Grid.SelectionChanged += (_, _) => BtnSelecionar.IsEnabled = Grid.SelectedItem != null;
    }

    private void TxtPesquisa_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var termo = TxtPesquisa.Text?.Trim();
        Grid.ItemsSource = string.IsNullOrWhiteSpace(termo)
            ? _todosOsPedidos
            : _todosOsPedidos.Where(p =>
                (p.Escola?.Nome?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Agrupamento?.Nome?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                p.Razao.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                p.Solicitante.Contains(termo, StringComparison.OrdinalIgnoreCase))
              .ToList();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is PedidoIntervencao) Confirmar();
    }

    private void Selecionar_Click(object sender, RoutedEventArgs e) => Confirmar();

    private void Confirmar()
    {
        if (Grid.SelectedItem is not PedidoIntervencao pedido) return;
        PedidoSelecionado = pedido;
        DialogResult = true;
        Close();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
