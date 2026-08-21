using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

/// <summary>
/// Módulo de Comunicações: gestão de todas as ligações de fibra (e outras) existentes nos
/// jardins-escola, estejam ou não integradas na rede/gestão centralizada da DISIA.
/// </summary>
public partial class ComunicacoesWindow : Window
{
    private List<Comunicacao> _todas = new();

    public ComunicacoesWindow()
    {
        InitializeComponent();
        Recarregar();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Filtro_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void ChkApenasNaoIntegrados_Changed(object sender, RoutedEventArgs e) => Recarregar();

    private void Recarregar()
    {
        var query = App.Db.Comunicacoes.Include(c => c.Escola).AsQueryable();
        if (ChkApenasNaoIntegrados.IsChecked == true)
            query = query.Where(c => !c.Integrado);

        _todas = query.OrderBy(c => c.Escola!.Nome).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        if (TxtPesquisa == null) return;

        IEnumerable<Comunicacao> resultado = _todas;

        var termo = TxtPesquisa?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            resultado = resultado.Where(c =>
                (c.Escola != null && c.Escola.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (c.Operadora != null && c.Operadora.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                c.TipoLigacao.Contains(termo, StringComparison.OrdinalIgnoreCase));
        }

        Grid.ItemsSource = resultado.ToList();
    }

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new ComunicacaoEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not Comunicacao comunicacao) return;

        var janela = new ComunicacaoEditWindow(comunicacao) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de comunicações",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Comunicacoes_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaComunicacoes(dialog.FileName);

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
