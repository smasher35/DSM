using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class EquipamentoAbatidoWindow : Window
{
    private List<EquipamentoAbatido> _todos = new();

    /// <summary>Lista atualmente visível na grelha (já com a pesquisa aplicada) — usada pelo
    /// "Relatório do Módulo" para o relatório refletir exatamente o que está a ser visto.</summary>
    private List<EquipamentoAbatido> _visiveis = new();

    public EquipamentoAbatidoWindow()
    {
        InitializeComponent();
        // Perfil Guest (Services/SessaoAtual.PodeEditar): acesso só de leitura a este módulo -
        // ver Services/PermissoesService.cs.
        LeiriaDISIA.Services.PermissoesService.AplicarSomenteLeituraSeGuest(BtnInserir);
        Recarregar();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Filtro_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void Recarregar()
    {
        _todos = App.Db.EquipamentosAbatidos.Include(a => a.Equipamento)
            .OrderByDescending(a => a.DataAbate).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        if (TxtPesquisa == null) return;

        IEnumerable<EquipamentoAbatido> resultado = _todos;

        var termo = TxtPesquisa?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            resultado = resultado.Where(a =>
                (a.Equipamento != null && a.Equipamento.NumeroSerie.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (a.DescricaoEquipamento != null && a.DescricaoEquipamento.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (a.EscolaOuLocal != null && a.EscolaOuLocal.Contains(termo, StringComparison.OrdinalIgnoreCase)));
        }

        Grid.ItemsSource = _visiveis = resultado.ToList();
    }

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new EquipamentoAbatidoEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not EquipamentoAbatido abate) return;

        var janela = new EquipamentoAbatidoEditWindow(abate) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        // (1.1) O relatório do módulo reflete exatamente o que está a ser visto na grelha (pesquisa
        // aplicada), em vez da lista completa de equipamento abatido.
        if (_visiveis.Count == 0)
        {
            MessageBox.Show("Não existe equipamento abatido a corresponder ao filtro atual para incluir no relatório.",
                "Sem dados para o relatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de equipamento abatido",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Equipamento_Abatido_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaEquipamentoAbatido(dialog.FileName, _visiveis.Select(a => a.Id).ToList());

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
