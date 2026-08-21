using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

/// <summary>
/// Janela auxiliar para pesquisar e selecionar um Equipamento pelo Nº de Série ou Nº de
/// Inventário, usada pela janela de Intervenções para associar equipamento intervencionado,
/// recolhido ou abatido.
/// </summary>
public partial class EquipamentoPickerWindow : Window
{
    private class LinhaEquipamento
    {
        public Equipamento Equipamento { get; set; } = null!;
        public string NumeroSerie => Equipamento.NumeroSerie;
        public string NumeroInventario => Equipamento.NumeroInventario;
        public string? Tipo => Equipamento.Tipo;
        public string MarcaModelo => $"{Equipamento.Marca} {Equipamento.Modelo}".Trim();
        public Escola? Escola => Equipamento.Escola;
    }

    private readonly List<LinhaEquipamento> _todos;
    private readonly List<LinhaEquipamento> _daEscola;
    private readonly int? _escolaIdFiltro;
    private readonly bool _restringirAEscola;

    public Equipamento? EquipamentoSelecionado { get; private set; }

    /// <param name="escolaIdFiltro">Se indicado, só aparece equipamento afeto a esta escola
    /// (o utilizador pode desmarcar "Mostrar equipamento de todas as escolas" para ver os restantes).</param>
    /// <param name="excluirIds">Ids de equipamento já adicionados noutra lista, para não aparecerem duplicados.</param>
    /// <param name="excluirJaRecolhido">7: quando verdadeiro, exclui equipamento que já tem uma
    /// recolha ativa (registo em <see cref="EquipamentoRecolhido"/> com Estado diferente de
    /// "Entregue") — impede recolher o mesmo equipamento mais do que uma vez em simultâneo. Só é
    /// usado no fluxo de registo de recolha; os restantes pickers mantêm o comportamento anterior.</param>
    public EquipamentoPickerWindow(int? escolaIdFiltro = null, IEnumerable<int>? excluirIds = null,
        bool excluirJaRecolhido = false, bool restringirAEscola = false)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _escolaIdFiltro = escolaIdFiltro;
        _restringirAEscola = restringirAEscola;

        var excluidos = (excluirIds ?? Enumerable.Empty<int>()).ToHashSet();
        if (excluirJaRecolhido)
        {
            var idsAtivamenteRecolhidos = App.Db.EquipamentosRecolhidos
                .Where(r => r.Estado != EstadosRecolha.Entregue)
                .Select(r => r.EquipamentoId)
                .ToHashSet();
            excluidos.UnionWith(idsAtivamenteRecolhidos);
        }

        var equipamentos = App.Db.Equipamentos.Include(e => e.Escola)
            .Where(e => e.Estado != EstadosEquipamento.Abatido && !excluidos.Contains(e.Id))
            .ToList();

        _todos = equipamentos
            .OrderByDescending(e => _escolaIdFiltro != null && e.EscolaId == _escolaIdFiltro)
            .ThenBy(e => e.NumeroSerie)
            .Select(e => new LinhaEquipamento { Equipamento = e })
            .ToList();

        _daEscola = _escolaIdFiltro == null
            ? _todos
            : _todos.Where(l => l.Equipamento.EscolaId == _escolaIdFiltro).ToList();

        if (_escolaIdFiltro != null)
        {
            ChkTodasEscolas.Visibility = _restringirAEscola ? Visibility.Collapsed : Visibility.Visible;
            TxtSemEquipamentoEscola.Visibility = _daEscola.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        Grid.ItemsSource = _daEscola;
    }

    private List<LinhaEquipamento> ListaBase => _escolaIdFiltro == null || (!_restringirAEscola && ChkTodasEscolas.IsChecked == true)
        ? _todos
        : _daEscola;

    private void ChkTodasEscolas_Changed(object sender, RoutedEventArgs e) => TxtPesquisa_TextChanged(sender, null!);

    private void TxtPesquisa_TextChanged(object sender, TextChangedEventArgs e)
    {
        var termo = TxtPesquisa.Text.Trim();
        var baseLista = ListaBase;
        Grid.ItemsSource = string.IsNullOrWhiteSpace(termo)
            ? baseLista
            : baseLista.Where(l =>
                l.NumeroSerie.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                l.NumeroInventario.Contains(termo, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void ConfirmarSelecao()
    {
        if (Grid.SelectedItem is LinhaEquipamento linha)
        {
            EquipamentoSelecionado = linha.Equipamento;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Selecione um equipamento da lista.", "Nenhum equipamento selecionado",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ConfirmarSelecao();

    private void Selecionar_Click(object sender, RoutedEventArgs e) => ConfirmarSelecao();

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
