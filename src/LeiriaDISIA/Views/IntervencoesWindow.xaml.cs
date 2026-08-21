using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class IntervencoesWindow : Window
{
    private static readonly string[] NomesMeses =
    {
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    };

    public IntervencoesWindow()
    {
        InitializeComponent();

        var anoAtual = DateTime.Today.Year;
        CmbAno.ItemsSource = Enumerable.Range(anoAtual - 3, 6).ToList();
        CmbAno.SelectedItem = anoAtual;

        var meses = new List<string> { "(Todos)" };
        meses.AddRange(NomesMeses);
        CmbMes.ItemsSource = meses;
        CmbMes.SelectedIndex = DateTime.Today.Month;

        var agrupamentos = new List<Agrupamento> { new() { Id = 0, Nome = "(Todos)" } };
        agrupamentos.AddRange(App.Db.Agrupamentos.OrderBy(a => a.Nome));
        CmbAgrupamentoFiltro.ItemsSource = agrupamentos;
        CmbAgrupamentoFiltro.SelectedIndex = 0;

        // (4.1) Legenda dos quadrados de cor da coluna "Categorias"
        LegendaCategorias.ItemsSource = App.Db.CategoriasIntervencao.OrderBy(c => c.Nome).ToList();

        Recarregar();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Filtro_Changed(object sender, SelectionChangedEventArgs e) => Recarregar();
    private void Filtro_TextChanged(object sender, TextChangedEventArgs e) => Recarregar();

    private void Recarregar()
    {
        if (CmbAno == null || Grid == null) return;

        var ano = (int?)CmbAno.SelectedItem ?? DateTime.Today.Year;
        var mesIndex = CmbMes.SelectedIndex;
        var agrupamentoSel = CmbAgrupamentoFiltro.SelectedItem as Agrupamento;

        var query = App.Db.Intervencoes
            .Include(i => i.Escola)
            .Include(i => i.Agrupamento)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .Where(i => i.Ano == ano)
            .AsQueryable();

        if (mesIndex > 0) query = query.Where(i => i.Mes == mesIndex);
        if (agrupamentoSel != null && agrupamentoSel.Id != 0) query = query.Where(i => i.AgrupamentoId == agrupamentoSel.Id);

        // Aplicar pesquisa
        var termo = TxtPesquisa?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            query = query.Where(i =>
                (i.Escola != null && i.Escola.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (i.Descricao != null && i.Descricao.Contains(termo, StringComparison.OrdinalIgnoreCase)));
        }

        Grid.ItemsSource = query.OrderByDescending(i => i.Data).ToList();
    }

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new IntervencaoEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not Intervencao intervencao) return;

        var janela = new IntervencaoEditWindow(intervencao) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int?)CmbAno.SelectedItem ?? DateTime.Today.Year;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de intervenções",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Intervencoes_{ano}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaIntervencoes(dialog.FileName, ano);

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
