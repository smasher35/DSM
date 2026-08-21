using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class AgrupamentosWindow : Window
{
    private Agrupamento? _selecionado;
    private List<Agrupamento> _todos = new();

    public AgrupamentosWindow()
    {
        InitializeComponent();
        Recarregar();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Filtro_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void Recarregar()
    {
        _todos = App.Db.Agrupamentos.Include(a => a.Escolas).OrderBy(a => a.Nome).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        if (TxtPesquisa == null) return;

        IEnumerable<Agrupamento> resultado = _todos;

        var termo = TxtPesquisa?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            resultado = resultado.Where(a =>
                (a.CodAgrupamento.ToString().Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (a.Nome != null && a.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)));
        }

        Grid.ItemsSource = resultado.ToList();
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

        CarregarEscolasDoAgrupamento();
    }

    private void CarregarEscolasDoAgrupamento()
    {
        if (_selecionado == null) return;
        TxtTituloEscolas.Text = $"Escolas do Agrupamento {_selecionado.Nome}";
        BtnAdicionarEscola.IsEnabled = true;
        GridEscolas.ItemsSource = App.Db.Escolas
            .Where(e => e.AgrupamentoId == _selecionado.Id && e.Estado != EstadosEscola.Desativada)
            .OrderBy(e => e.Nome)
            .ToList();
    }

    private void GridEscolas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridEscolas.SelectedItem is not Escola escola) return;

        var janela = new EscolaEditWindow(escola) { Owner = this };
        janela.ShowDialog();

        if (janela.Sucesso)
        {
            CarregarEscolasDoAgrupamento();
            Recarregar();
        }
    }

    private void AdicionarEscola_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionado == null) return;

        var janela = new EscolaEditWindow(null, _selecionado.Id) { Owner = this };
        janela.ShowDialog();

        if (janela.Sucesso)
        {
            CarregarEscolasDoAgrupamento();
            Recarregar();
        }
    }

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new AgrupamentoEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AbrirEdicaoSelecionado();

    private void EditarSelecionado_Click(object sender, RoutedEventArgs e) => AbrirEdicaoSelecionado();

    private void AbrirEdicaoSelecionado()
    {
        if (_selecionado == null)
        {
            MessageBox.Show("Selecione um agrupamento para editar.", "Ação necessária",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var janela = new AgrupamentoEditWindow(_selecionado) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
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
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de agrupamentos",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Agrupamentos_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaAgrupamentos(dialog.FileName);

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
