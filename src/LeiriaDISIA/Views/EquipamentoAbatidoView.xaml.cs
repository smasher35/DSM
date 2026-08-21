using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

public partial class EquipamentoAbatidoView : UserControl
{
    private EquipamentoAbatido? _selecionado;
    private static readonly string[] StatusComuns = { "Abatido", "Em processo de abate", "Doado", "Reciclado" };

    public EquipamentoAbatidoView()
    {
        InitializeComponent();
        CmbStatus.ItemsSource = StatusComuns;

        var equipamentosDisponiveis = new List<Equipamento> { new() { Id = 0, NumeroSerie = "(nenhum / não cadastrado)" } };
        equipamentosDisponiveis.AddRange(App.Db.Equipamentos.Where(e => e.Estado != EstadosEquipamento.Abatido)
            .OrderBy(e => e.NumeroSerie));
        CmbEquipamento.ItemsSource = equipamentosDisponiveis;
        CmbEquipamento.DisplayMemberPath = "NumeroSerie";

        Recarregar();
        Novo_Click(this, new RoutedEventArgs());
    }

    private void Recarregar()
    {
        Grid.ItemsSource = App.Db.EquipamentosAbatidos.Include(a => a.Equipamento)
            .OrderByDescending(a => a.DataAbate).ToList();
    }

    private void CmbEquipamento_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbEquipamento.SelectedItem is Equipamento eq && eq.Id != 0)
        {
            TxtDescricao.Text = $"{eq.Tipo} {eq.Marca} {eq.Modelo}".Trim();
            TxtNumeroSerie.Text = eq.NumeroSerie;
            TxtNumeroInventario.Text = eq.NumeroInventario;
            TxtEscolaOuLocal.Text = eq.Escola?.Nome ?? eq.LocalNaoEscolar;
        }
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionado = Grid.SelectedItem as EquipamentoAbatido;
        if (_selecionado == null) return;

        CmbEquipamento.SelectedItem = ((List<Equipamento>)CmbEquipamento.ItemsSource)
            .FirstOrDefault(x => x.Id == (_selecionado.EquipamentoId ?? 0));
        TxtEscolaOuLocal.Text = _selecionado.EscolaOuLocal;
        TxtDescricao.Text = _selecionado.DescricaoEquipamento;
        TxtNumeroSerie.Text = _selecionado.NumeroSerie;
        TxtNumeroInventario.Text = _selecionado.NumeroInventario;
        DpAbate.SelectedDate = _selecionado.DataAbate;
        CmbStatus.Text = _selecionado.Status;
        TxtObservacoes.Text = _selecionado.Observacoes;
    }

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        _selecionado = null;
        Grid.SelectedItem = null;
        CmbEquipamento.SelectedIndex = 0;
        TxtEscolaOuLocal.Clear();
        TxtDescricao.Clear();
        TxtNumeroSerie.Clear();
        TxtNumeroInventario.Clear();
        DpAbate.SelectedDate = DateTime.Today;
        CmbStatus.Text = "Abatido";
        TxtObservacoes.Clear();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDescricao.Text))
        {
            MessageBox.Show("Descreva o equipamento a abater.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var equipamentoSel = CmbEquipamento.SelectedItem as Equipamento;
        var equipamentoId = equipamentoSel?.Id == 0 ? null : equipamentoSel?.Id;

        if (_selecionado == null)
        {
            _selecionado = new EquipamentoAbatido();
            App.Db.EquipamentosAbatidos.Add(_selecionado);
        }

        _selecionado.EquipamentoId = equipamentoId;
        _selecionado.EscolaOuLocal = TxtEscolaOuLocal.Text;
        _selecionado.DescricaoEquipamento = TxtDescricao.Text.Trim();
        _selecionado.NumeroSerie = string.IsNullOrWhiteSpace(TxtNumeroSerie.Text) ? null : TxtNumeroSerie.Text.Trim();
        _selecionado.NumeroInventario = string.IsNullOrWhiteSpace(TxtNumeroInventario.Text) ? null : TxtNumeroInventario.Text.Trim();
        _selecionado.DataAbate = DpAbate.SelectedDate ?? DateTime.Today;
        _selecionado.Status = CmbStatus.Text;
        _selecionado.Observacoes = TxtObservacoes.Text;

        App.Db.SaveChanges();

        if (equipamentoId != null)
        {
            var equipamento = App.Db.Equipamentos.First(x => x.Id == equipamentoId);
            equipamento.Estado = EstadosEquipamento.Abatido;
            App.Db.SaveChanges();
        }

        Recarregar();
        Novo_Click(sender, e);
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionado == null) return;
        if (MessageBox.Show("Eliminar este registo de abate?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        App.Db.EquipamentosAbatidos.Remove(_selecionado);
        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }
}
