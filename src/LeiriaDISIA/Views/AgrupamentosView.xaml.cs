using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class AgrupamentosView : UserControl
{
    private Agrupamento? _selecionado;

    public AgrupamentosView()
    {
        InitializeComponent();
        Recarregar();
    }

    private void Recarregar()
    {
        Grid.ItemsSource = App.Db.Agrupamentos
            .Include(a => a.Escolas)
            .OrderBy(a => a.Nome)
            .ToList();
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionado = Grid.SelectedItem as Agrupamento;
        if (_selecionado == null)
        {
            GridEscolas.ItemsSource = null;
            BtnAdicionarEscola.IsEnabled = false;
            TxtTituloEscolas.Text = "Escolas do agrupamento selecionado";
            return;
        }

        TxtCodigo.Text = _selecionado.CodAgrupamento.ToString();
        TxtNome.Text = _selecionado.Nome;
        TxtObservacoes.Text = _selecionado.Observacoes;

        CarregarEscolasDoAgrupamento();
    }

    private void CarregarEscolasDoAgrupamento()
    {
        if (_selecionado == null) return;

        TxtTituloEscolas.Text = $"Escolas do Agrupamento {_selecionado.Nome}";
        BtnAdicionarEscola.IsEnabled = true;
        GridEscolas.ItemsSource = App.Db.Escolas
            .Where(e => e.AgrupamentoId == _selecionado.Id)
            .OrderBy(e => e.Nome)
            .ToList();
    }

    private void GridEscolas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridEscolas.SelectedItem is not Escola escola) return;

        var janela = new EscolaEditWindow(escola) { Owner = Window.GetWindow(this) };
        janela.ShowDialog();

        if (janela.Sucesso)
        {
            CarregarEscolasDoAgrupamento();
            Recarregar(); // atualiza o total de escolas na grelha de agrupamentos
        }
    }

    private void AdicionarEscola_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionado == null) return;

        var janela = new EscolaEditWindow(null, _selecionado.Id) { Owner = Window.GetWindow(this) };
        janela.ShowDialog();

        if (janela.Sucesso)
        {
            CarregarEscolasDoAgrupamento();
            Recarregar();
        }
    }

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        _selecionado = null;
        TxtCodigo.Clear();
        TxtNome.Clear();
        TxtObservacoes.Clear();
        Grid.SelectedItem = null;
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtNome.Text))
        {
            MessageBox.Show("Indique o nome do agrupamento.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtCodigo.Text, out var codigo))
        {
            MessageBox.Show("O código do agrupamento tem de ser numérico.", "Dados inválidos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_selecionado == null)
        {
            App.Db.Agrupamentos.Add(new Models.Agrupamento
            {
                CodAgrupamento = codigo,
                Nome = TxtNome.Text.Trim(),
                Observacoes = TxtObservacoes.Text
            });
        }
        else
        {
            _selecionado.CodAgrupamento = codigo;
            _selecionado.Nome = TxtNome.Text.Trim();
            _selecionado.Observacoes = TxtObservacoes.Text;
        }

        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionado == null) return;

        var totalEscolas = App.Db.Escolas.Count(x => x.AgrupamentoId == _selecionado.Id);
        if (totalEscolas > 0)
        {
            MessageBox.Show(
                $"Não é possível eliminar: existem {totalEscolas} escola(s) associada(s) a este agrupamento.",
                "Não permitido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Eliminar o agrupamento '{_selecionado.Nome}'?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        App.Db.Agrupamentos.Remove(_selecionado);
        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }

    private void GerarRelatorio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de agrupamentos",
            Filter = "Ficheiro de texto (*.txt)|*.txt",
            FileName = $"Relatorio_Agrupamentos_{DateTime.Today:yyyyMMdd}.txt"
        };
        if (dialog.ShowDialog() != true) return;

        var agrupamentos = App.Db.Agrupamentos.Include(a => a.Escolas).OrderBy(a => a.Nome).ToList();
        using var writer = new StreamWriter(dialog.FileName);
        writer.WriteLine("RELATÓRIO DE AGRUPAMENTOS - CONCELHO DE LEIRIA");
        writer.WriteLine(new string('=', 50));
        foreach (var a in agrupamentos)
        {
            writer.WriteLine($"\n{a.Nome} (Cód. {a.CodAgrupamento}) — {a.Escolas.Count} escola(s)");
            foreach (var esc in a.Escolas.OrderBy(x => x.Nome))
                writer.WriteLine($"   - {esc.Nome} ({esc.Localidade})");
        }
        MessageBox.Show("Relatório gerado com sucesso.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
