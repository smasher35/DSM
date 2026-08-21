using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

/// <summary>
/// Item 3.3: pequeno diálogo para escolher a escola (opcional), o responsável pelo inventário e se
/// a folha deve sair em branco ou pré-preenchida com o equipamento já cadastrado dessa escola,
/// antes de gerar o PDF em <see cref="RelatorioService.GerarFolhaInventarioPdf"/>.
///
/// Aberto a partir do módulo Equipamento Informático (ver Views/EquipamentosWindow.xaml.cs), onde o
/// utilizador já está no contexto certo para escolher a escola antes de gerar — ver justificação
/// completa no resumo de entrega do item 3.3.
///
/// A geração corre em duas fases, para nunca bloquear a janela (nem impedir de a fechar) enquanto
/// o PDF está a ser desenhado:
/// 1. Consulta à base de dados (rápida) — direta na UI thread, via
///    <see cref="RelatorioService.ObterDadosFolhaInventario"/>.
/// 2. Desenho do PDF (mais lento) — feito em <c>Task.Run</c> numa thread em segundo plano, através
///    de <see cref="RelatorioService.GerarFolhaInventarioPdf"/>, que não toca na base de dados (o
///    <c>AppDbContext</c> do Entity Framework Core não é thread-safe, por isso a consulta tem de
///    ficar sempre na UI thread).
/// </summary>
public partial class FolhaInventarioWindow : Window
{
    /// <summary>Item "placeholder" no topo do combo, para representar "nenhuma escola escolhida"
    /// sem precisar de um SelectedItem nulo (que complicaria o binding do DisplayMemberPath).</summary>
    private static readonly Escola SemEscola = new() { Id = 0, Nome = "— Nenhuma (folha genérica em branco) —" };

    /// <summary>Verdadeiro enquanto o PDF está a ser desenhado em segundo plano — usado para
    /// desativar os botões (evitar cliques duplicados) e para impedir o fecho "a meio" da janela
    /// (ver <see cref="Window_Closing"/>), já que fechar a janela não cancela a Task em curso.</summary>
    private bool _aGerar;

    public FolhaInventarioWindow()
    {
        InitializeComponent();
        Closing += Window_Closing;

        var escolas = App.Db.Escolas
            .Where(e => e.Estado != EstadosEscola.Desativada)
            .OrderBy(e => e.Nome)
            .ToList();
        escolas.Insert(0, SemEscola);

        CmbEscola.ItemsSource = escolas;
        CmbEscola.SelectedItem = SemEscola;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_aGerar) return;
        e.Cancel = true;
        MessageBox.Show("A folha de inventário ainda está a ser gerada — aguarde um momento.",
            "A gerar…", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CmbEscola_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var temEscola = CmbEscola.SelectedItem is Escola { Id: not 0 };
        RbPreenchida.IsEnabled = temEscola;
        if (!temEscola) RbEmBranco.IsChecked = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Gera a folha para um ficheiro temporário e pede ao Windows para a imprimir
    /// diretamente na impressora predefinida (verbo "print", tratado pela aplicação associada a
    /// .pdf — normalmente o Edge/Adobe Reader), sem obrigar a abrir o PDF manualmente e procurar
    /// "Imprimir" lá dentro.</summary>
    private async void Imprimir_Click(object sender, RoutedEventArgs e)
    {
        var caminhoTemp = Path.Combine(Path.GetTempPath(), $"FolhaInventario_{Guid.NewGuid():N}.pdf");

        if (!await GerarPdfAsync(caminhoTemp)) return;

        try
        {
            Process.Start(new ProcessStartInfo(caminhoTemp) { UseShellExecute = true, Verb = "print" });
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Não foi possível enviar automaticamente para a impressora (pode não haver nenhuma aplicação de PDF " +
                $"associada ao verbo \"Imprimir\" neste computador).\n\nO ficheiro foi gerado em:\n{caminhoTemp}\n\n" +
                $"Detalhe: {ex.Message}\n\nPode abri-lo manualmente e imprimir a partir daí.",
                "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Gerar_Click(object sender, RoutedEventArgs e)
    {
        var escolaSelecionada = CmbEscola.SelectedItem as Escola;
        var nomeSugerido = escolaSelecionada is { Id: not 0 }
            ? $"Folha_Inventario_{escolaSelecionada.Nome.Replace(" ", "_")}"
            : $"Folha_Inventario_{DateTime.Today:yyyyMMdd}";

        var dialog = new SaveFileDialog
        {
            Title = "Guardar Folha de Inventário",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = nomeSugerido
        };
        if (dialog.ShowDialog() != true) return;

        if (!await GerarPdfAsync(dialog.FileName)) return;

        var abrir = MessageBox.Show("Folha de Inventário gerada com sucesso. Deseja abri-la agora?",
            "Concluído", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (abrir == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });

        Close();
    }

    /// <summary>Lógica de geração partilhada por "Imprimir" e "Gerar / Guardar PDF" — só muda o
    /// caminho de destino (ficheiro temporário vs. escolhido pelo utilizador). A consulta à base de
    /// dados corre aqui, na UI thread (rápida); o desenho do PDF em si corre em segundo plano, para
    /// a janela nunca ficar sem resposta enquanto o QuestPDF trabalha.</summary>
    private async Task<bool> GerarPdfAsync(string caminhoDestino)
    {
        var escolaSelecionada = CmbEscola.SelectedItem as Escola;
        var escolaId = escolaSelecionada is { Id: not 0 } ? escolaSelecionada.Id : (int?)null;
        var preFilled = RbPreenchida.IsChecked == true;
        var responsavel = TxtResponsavel.Text;

        DefinirAGerar(true);
        try
        {
            var servico = new RelatorioService(App.Db);

            // Consulta (rápida) na UI thread — o AppDbContext não pode ser acedido a partir da
            // Task.Run abaixo.
            var (escola, equipamentos) = servico.ObterDadosFolhaInventario(escolaId, preFilled);

            // Desenho do PDF (mais lento) em segundo plano, para não bloquear a janela.
            await Task.Run(() => servico.GerarFolhaInventarioPdf(caminhoDestino, escola, equipamentos, responsavel));

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar a folha de inventário:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            DefinirAGerar(false);
        }
    }

    private void DefinirAGerar(bool aGerar)
    {
        _aGerar = aGerar;
        TxtEstado.Visibility = aGerar ? Visibility.Visible : Visibility.Collapsed;
        BtnGerar.IsEnabled = !aGerar;
        BtnImprimir.IsEnabled = !aGerar;
        BtnCancelar.IsEnabled = !aGerar;
        CmbEscola.IsEnabled = !aGerar;
        TxtResponsavel.IsEnabled = !aGerar;
        RbEmBranco.IsEnabled = !aGerar;
        RbPreenchida.IsEnabled = !aGerar && CmbEscola.SelectedItem is Escola { Id: not 0 };
    }
}
