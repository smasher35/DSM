using System.IO;
using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Services;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class RelatoriosView : UserControl
{
    private static readonly string[] NomesMeses =
    {
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    };

    public RelatoriosView()
    {
        InitializeComponent();

        var anoAtual = DateTime.Today.Year;
        CmbAno.ItemsSource = Enumerable.Range(anoAtual - 3, 6).ToList();
        CmbAno.SelectedItem = anoAtual;

        CmbMes.ItemsSource = NomesMeses;
        CmbMes.SelectedIndex = DateTime.Today.Month - 1;

        TxtTelefone.Text = "966 589 120";
        TxtEmail.Text = "paulo@cm-leiria.pt";
    }

    private void GerarMensal_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        var mes = CmbMes.SelectedIndex + 1;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório mensal",
            Filter = "Documento Word (*.docx)|*.docx",
            FileName = $"Relatorio_Atividades_DISIA_{NomesMeses[mes - 1]}_{ano}.docx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new RelatorioService(App.Db);
            servico.GerarRelatorioMensal(ano, mes, TxtAutor.Text, TxtDivisao.Text,
                TxtTelefone.Text, TxtEmail.Text, dialog.FileName);

            var abrir = MessageBox.Show("Relatório gerado com sucesso. Deseja abri-lo agora?",
                "Concluído", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (abrir == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar o relatório:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GerarAnual_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório anual",
            Filter = "Documento Word (*.docx)|*.docx",
            FileName = $"Relatorio_Anual_DISIA_{ano}.docx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new RelatorioService(App.Db);
            servico.GerarRelatorioAnual(ano, TxtAutor.Text, TxtDivisao.Text,
                TxtTelefone.Text, TxtEmail.Text, dialog.FileName);

            var abrir = MessageBox.Show("Relatório gerado com sucesso. Deseja abri-lo agora?",
                "Concluído", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (abrir == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar o relatório:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GerarListaEscolas_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar lista total de escolas",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Total_Escolas_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new RelatorioService(App.Db);
            servico.GerarListaTotalEscolas(dialog.FileName);

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
