using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class ContactosWindow : Window
{
    private List<Contacto> _todos = new();

    public ContactosWindow()
    {
        InitializeComponent();
        Recarregar();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Recarregar()
    {
        _todos = App.Db.Contactos.Include(c => c.Escola).OrderBy(c => c.Nome).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        var termo = TxtPesquisa?.Text?.Trim();
        Grid.ItemsSource = string.IsNullOrWhiteSpace(termo)
            ? _todos
            : _todos.Where(c =>
                c.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                (c.Escola?.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.EntidadeExterna?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Funcao?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false)
              ).ToList();
    }

    private void Filtro_Changed(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new ContactoEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not Contacto contacto) return;

        var janela = new ContactoEditWindow(contacto) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de contactos",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Contactos_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaContactos(dialog.FileName);

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
