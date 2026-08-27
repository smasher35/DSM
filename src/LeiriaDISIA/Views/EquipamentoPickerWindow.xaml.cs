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

        /// <summary>Verdadeiro quando o equipamento está fisicamente na escola a que está afeto —
        /// "Em Serviço" é o único estado que significa isso; qualquer outro (Recolhido, Em
        /// Reparação, Reparado, Aguarda Entrega, Em Armazém) significa que está algures na DISIA,
        /// fora da escola, independentemente de ter ou não um registo ativo em
        /// <see cref="Models.EquipamentoRecolhido"/> - é este campo (Estado do próprio
        /// Equipamento), e não esse registo, que reflete de forma fiável a localização atual.</summary>
        public bool NaEscola => Equipamento.Estado.Equals(EstadosEquipamento.EmServico, StringComparison.OrdinalIgnoreCase);

        public string LocalizacaoTexto => NaEscola ? "Na Escola" : "Na DISIA";
        public string LocalizacaoCorHex => NaEscola ? "#22C55E" : "#F59E0B";
    }

    private readonly List<LinhaEquipamento> _todos;
    private readonly List<LinhaEquipamento> _daEscola;
    private readonly int? _escolaIdFiltro;
    private readonly bool _restringirAEscola;
    private readonly bool _exigirNaEscola;

    public Equipamento? EquipamentoSelecionado { get; private set; }

    /// <param name="escolaIdFiltro">Se indicado, só aparece equipamento afeto a esta escola
    /// (o utilizador pode desmarcar "Mostrar equipamento de todas as escolas" para ver os restantes).</param>
    /// <param name="excluirIds">Ids de equipamento já adicionados noutra lista, para não aparecerem duplicados.</param>
    /// <param name="excluirJaRecolhido">7: quando verdadeiro, exclui equipamento que já tem uma
    /// recolha ativa (registo em <see cref="EquipamentoRecolhido"/> com Estado diferente de
    /// "Entregue") — impede recolher o mesmo equipamento mais do que uma vez em simultâneo. Só é
    /// usado no fluxo de registo de recolha; os restantes pickers mantêm o comportamento anterior.</param>
    /// <param name="exigirNaEscola">Quando verdadeiro, o equipamento continua visível na lista
    /// (com o badge "Na DISIA" a vermelho/laranja, para o utilizador perceber porquê), mas NÃO
    /// pode ser confirmado como seleção se não estiver fisicamente na escola (Estado diferente de
    /// "Em Serviço") — mostra uma mensagem a explicar em vez de deixar selecionar. Usado nos
    /// fluxos onde o equipamento tem mesmo de estar presente na escola: "Equipamento reparado no
    /// local" de uma Intervenção, e o registo de uma nova Recolha (não se pode recolher, nem
    /// reparar no local, equipamento que já não está lá).</param>
    public EquipamentoPickerWindow(int? escolaIdFiltro = null, IEnumerable<int>? excluirIds = null,
        bool excluirJaRecolhido = false, bool restringirAEscola = false, bool exigirNaEscola = false)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _escolaIdFiltro = escolaIdFiltro;
        _restringirAEscola = restringirAEscola;
        _exigirNaEscola = exigirNaEscola;

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
        if (Grid.SelectedItem is not LinhaEquipamento linha)
        {
            MessageBox.Show("Selecione um equipamento da lista.", "Nenhum equipamento selecionado",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_exigirNaEscola && !linha.NaEscola)
        {
            MessageBox.Show(
                $"O equipamento \"{linha.NumeroSerie}\" não está fisicamente na escola neste momento " +
                $"(estado atual: \"{linha.Equipamento.Estado}\", localização: Na DISIA) — não pode ser " +
                "selecionado. Só é possível escolher equipamento que esteja realmente na escola.",
                "Equipamento não está na escola", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        EquipamentoSelecionado = linha.Equipamento;
        DialogResult = true;
        Close();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ConfirmarSelecao();

    private void Selecionar_Click(object sender, RoutedEventArgs e) => ConfirmarSelecao();

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
