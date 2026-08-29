using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class EscolasWindow : Window
{
    private List<Escola> _todas = new();

    public EscolasWindow()
    {
        InitializeComponent();
        // Perfil Guest (Services/SessaoAtual.PodeEditar): acesso só de leitura a este módulo -
        // ver Services/PermissoesService.cs.
        LeiriaDISIA.Services.PermissoesService.AplicarSomenteLeituraSeGuest(BtnInserir);
        CarregarCombos();
        RecarregarGrid();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void CarregarCombos()
    {
        var agrupamentos = App.Db.Agrupamentos.OrderBy(a => a.Nome).ToList();
        var comFiltroTodos = new List<Agrupamento> { new() { Id = 0, Nome = "(Todos os agrupamentos)" } };
        comFiltroTodos.AddRange(agrupamentos);
        CmbFiltroAgrupamento.ItemsSource = comFiltroTodos;
        CmbFiltroAgrupamento.SelectedIndex = 0;
    }

    private void RecarregarGrid()
    {
        _todas = App.Db.Escolas.Include(e => e.Agrupamento).Where(e => e.Estado != EstadosEscola.Desativada).OrderBy(e => e.Nome).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        IEnumerable<Escola> resultado = _todas;

        if (CmbFiltroAgrupamento?.SelectedItem is Agrupamento ag && ag.Id != 0)
            resultado = resultado.Where(e => e.AgrupamentoId == ag.Id);

        var termo = TxtPesquisa?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            resultado = resultado.Where(e =>
                (e.Nome?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Localidade?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Freguesia?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.NomeAlternativo?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Grid.ItemsSource = resultado.ToList();
    }

    private void Filtro_Changed(object sender, SelectionChangedEventArgs e) => AplicarFiltro();
    private void Filtro_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new EscolaEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) RecarregarGrid();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not Escola escola) return;

        var janela = new EscolaEditWindow(escola) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) RecarregarGrid();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        // (4.1) O relatório deve refletir apenas o que está a ser visualizado: se houver um
        // agrupamento selecionado no filtro, o relatório é gerado só para esse agrupamento;
        // se o filtro estiver em "(Todos os agrupamentos)", mantém-se o relatório de todas as escolas.
        var agrupamentoSelecionado = CmbFiltroAgrupamento?.SelectedItem as Agrupamento;
        var filtrado = agrupamentoSelecionado is not null && agrupamentoSelecionado.Id != 0;

        var nomeFicheiroBase = filtrado
            ? $"Lista_Escolas_{LimparNomeFicheiro(agrupamentoSelecionado!.Nome)}"
            : "Lista_Total_Escolas";

        var dialog = new SaveFileDialog
        {
            Title = filtrado ? "Guardar lista de escolas do agrupamento" : "Guardar lista total de escolas",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"{nomeFicheiroBase}_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaTotalEscolas(dialog.FileName, filtrado ? agrupamentoSelecionado!.Id : null);

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

    private static string LimparNomeFicheiro(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "Agrupamento";
        var invalidos = Path.GetInvalidFileNameChars();
        var limpo = new string(nome.Where(c => !invalidos.Contains(c)).ToArray());
        return limpo.Replace(" ", "_");
    }
}
