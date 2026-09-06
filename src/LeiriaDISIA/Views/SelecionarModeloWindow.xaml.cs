using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;

namespace LeiriaDISIA.Views;

/// <summary>
/// Diálogo para escolher um "modelo base" já criado (ver Models/ModeloEquipamento.cs e
/// Views/ModelosEquipamentoWindow.xaml.cs), filtrado ao Tipo de Equipamento já escolhido em
/// Inserir/Editar Equipamento — chamado a partir do botão "Usar Modelo..." aí (ver
/// Views/EquipamentoEditWindow.xaml.cs). Mesmo padrão de diálogo simples de
/// Views/EscolherMesWindow.xaml.cs: construtor + <see cref="Perguntar"/> estático que mostra o
/// diálogo e devolve o modelo escolhido, ou null se o utilizador cancelar.
/// </summary>
public partial class SelecionarModeloWindow : Window
{
    private readonly List<ModeloEquipamento> _todos;
    public ModeloEquipamento? Escolhido { get; private set; }

    public SelecionarModeloWindow(string tipo, List<ModeloEquipamento> modelosDoTipo)
    {
        InitializeComponent();

        TxtTitulo.Text = $"Selecionar Modelo — {tipo}";
        _todos = modelosDoTipo;

        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        var termo = TxtPesquisa.Text?.Trim();
        var resultado = string.IsNullOrWhiteSpace(termo)
            ? _todos
            : _todos.Where(m =>
                (m.Nome != null && m.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (m.Marca != null && m.Marca.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (m.Modelo != null && m.Modelo.Contains(termo, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        ListaModelos.ItemsSource = resultado;
        TxtSemModelos.Visibility = _todos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtPesquisa_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void ListaModelos_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        BtnSelecionar.IsEnabled = ListaModelos.SelectedItem != null;

    private void ListaModelos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ListaModelos.SelectedItem is ModeloEquipamento) Selecionar_Click(sender, e);
    }

    private void Selecionar_Click(object sender, RoutedEventArgs e)
    {
        if (ListaModelos.SelectedItem is not ModeloEquipamento modelo) return;
        Escolhido = modelo;
        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Mostra o diálogo, já filtrado aos modelos ativos do Tipo indicado, e devolve o
    /// modelo escolhido — ou null se o utilizador cancelar ou não houver nenhum modelo para esse
    /// tipo.</summary>
    public static ModeloEquipamento? Perguntar(Window owner, string tipo, List<ModeloEquipamento> todosOsModelos)
    {
        var modelosDoTipo = todosOsModelos.Where(m => m.Ativo && m.Tipo == tipo).ToList();
        var janela = new SelecionarModeloWindow(tipo, modelosDoTipo) { Owner = owner };
        return janela.ShowDialog() == true ? janela.Escolhido : null;
    }
}
