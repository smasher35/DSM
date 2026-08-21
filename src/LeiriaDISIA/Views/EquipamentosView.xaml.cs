using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

public partial class EquipamentosView : System.Windows.Controls.UserControl
{
    private Equipamento? _selecionado;
    private List<Equipamento> _todos = new();

    private static readonly string[] TiposComuns =
    {
        "Computador de Secretária", "Portátil", "Monitor", "Impressora", "Multifunções",
        "Switch", "Router", "Access Point", "Câmara CCTV", "Projetor", "Quadro Interativo",
        "Tablet", "Servidor", "UPS/No-break", "Telefone IP", "Outro"
    };

    public EquipamentosView()
    {
        InitializeComponent();
        CmbTipo.ItemsSource = TiposComuns;
        CmbEstado.ItemsSource = new[]
        {
            EstadosEquipamento.EmServico, EstadosEquipamento.Recolhido, EstadosEquipamento.EmReparacao,
            EstadosEquipamento.Reparado, EstadosEquipamento.AguardaEntrega,
            EstadosEquipamento.EmArmazem, EstadosEquipamento.Abatido
        };
        CmbEscola.ItemsSource = App.Db.Escolas.OrderBy(e => e.Nome).ToList();
        Recarregar();
        Novo_Click(this, new RoutedEventArgs());
    }

    private void Recarregar()
    {
        _todos = App.Db.Equipamentos.Include(e => e.Escola).OrderByDescending(e => e.Id).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        var termo = TxtPesquisa?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(termo))
        {
            Grid.ItemsSource = _todos;
            return;
        }

        Grid.ItemsSource = _todos.Where(e =>
            e.NumeroSerie.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
            e.NumeroInventario.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
            (e.Tipo?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (e.Marca?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (e.Escola?.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false)
        ).ToList();
    }

    private void Filtro_Changed(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionado = Grid.SelectedItem as Equipamento;
        if (_selecionado == null) return;

        TxtNumeroSerie.Text = _selecionado.NumeroSerie;
        TxtNumeroInventario.Text = _selecionado.NumeroInventario;
        CmbTipo.Text = _selecionado.Tipo;
        TxtMarca.Text = _selecionado.Marca;
        TxtModelo.Text = _selecionado.Modelo;
        DpAquisicao.SelectedDate = _selecionado.DataAquisicao;
        TxtValor.Text = _selecionado.ValorAquisicao?.ToString();
        TxtFornecedor.Text = _selecionado.Fornecedor;
        CmbEscola.SelectedItem = ((List<Escola>)CmbEscola.ItemsSource).FirstOrDefault(x => x.Id == _selecionado.EscolaId);
        TxtLocalNaoEscolar.Text = _selecionado.LocalNaoEscolar;
        CmbEstado.SelectedItem = _selecionado.Estado;
        TxtObservacoes.Text = _selecionado.Observacoes;
    }

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        _selecionado = null;
        Grid.SelectedItem = null;
        TxtNumeroSerie.Clear();
        TxtNumeroInventario.Clear();
        CmbTipo.Text = "";
        TxtMarca.Clear();
        TxtModelo.Clear();
        DpAquisicao.SelectedDate = null;
        TxtValor.Clear();
        TxtFornecedor.Clear();
        CmbEscola.SelectedItem = null;
        TxtLocalNaoEscolar.Clear();
        CmbEstado.SelectedItem = EstadosEquipamento.EmServico;
        TxtObservacoes.Clear();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtNumeroSerie.Text) || string.IsNullOrWhiteSpace(TxtNumeroInventario.Text))
        {
            MessageBox.Show("O Número de Série e o Número de Inventário são obrigatórios.",
                "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var duplicado = _todos.Any(x =>
            (_selecionado == null || x.Id != _selecionado.Id) &&
            (x.NumeroSerie.Equals(TxtNumeroSerie.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
             x.NumeroInventario.Equals(TxtNumeroInventario.Text.Trim(), StringComparison.OrdinalIgnoreCase)));
        if (duplicado)
        {
            MessageBox.Show("Já existe um equipamento com o mesmo Número de Série ou de Inventário.",
                "Duplicado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        decimal? valor = decimal.TryParse(TxtValor.Text, out var v) ? v : null;

        if (_selecionado == null)
        {
            _selecionado = new Equipamento();
            App.Db.Equipamentos.Add(_selecionado);
        }

        _selecionado.NumeroSerie = TxtNumeroSerie.Text.Trim();
        _selecionado.NumeroInventario = TxtNumeroInventario.Text.Trim();
        _selecionado.Tipo = CmbTipo.Text;
        _selecionado.Marca = TxtMarca.Text;
        _selecionado.Modelo = TxtModelo.Text;
        _selecionado.DataAquisicao = DpAquisicao.SelectedDate;
        _selecionado.ValorAquisicao = valor;
        _selecionado.Fornecedor = TxtFornecedor.Text;
        _selecionado.EscolaId = (CmbEscola.SelectedItem as Escola)?.Id;
        _selecionado.LocalNaoEscolar = TxtLocalNaoEscolar.Text;
        _selecionado.Estado = CmbEstado.SelectedItem as string ?? EstadosEquipamento.EmServico;
        _selecionado.Observacoes = TxtObservacoes.Text;

        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionado == null) return;
        if (MessageBox.Show("Eliminar este equipamento?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        App.Db.Equipamentos.Remove(_selecionado);
        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }
}
