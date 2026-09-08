using System.Windows;
using System.Windows.Input;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

/// <summary>
/// Histórico de intervenções técnicas realizadas numa escola, acessível a partir do botão
/// "📖 Histórico de Intervenções" em Views/EscolaEditWindow.xaml — só visível/ativo para uma
/// escola já existente (uma escola nova, ainda sem Id, não pode ter nenhuma intervenção
/// associada). Duplo-clique numa linha abre essa intervenção para consulta/edição completa (ver
/// Views/IntervencaoEditWindow.xaml.cs), tal como na grelha principal do módulo Intervenções.
/// </summary>
public partial class HistoricoIntervencoesEscolaWindow : Window
{
    private readonly Escola _escola;
    private List<Intervencao> _intervencoes = new();

    public HistoricoIntervencoesEscolaWindow(Escola escola)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _escola = escola;
        TxtTitulo.Text = $"Histórico de Intervenções — {escola.Nome}";

        Carregar();
    }

    private void Carregar()
    {
        _intervencoes = App.Db.Intervencoes
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .Where(i => i.EscolaId == _escola.Id)
            .OrderByDescending(i => i.Data)
            .ToList();

        Grid.ItemsSource = _intervencoes;

        var fechadas = _intervencoes.Count(i => i.Estado == EstadoIntervencao.Fechada);
        TxtResumo.Text = _intervencoes.Count == 0
            ? "Sem intervenções registadas para esta escola."
            : $"{_intervencoes.Count} intervenção(ões) no total, {fechadas} fechada(s).";

        TxtSemIntervencoes.Visibility = _intervencoes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnGerarPdf.IsEnabled = _intervencoes.Count > 0;
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not Intervencao intervencao) return;

        var janela = new IntervencaoEditWindow(intervencao) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Carregar();
    }

    private void GerarPdf_Click(object sender, RoutedEventArgs e)
    {
        // O botão já fica desativado sem intervenções (ver Carregar()); esta verificação é só
        // uma segunda salvaguarda, tal como nos relatórios de módulo (item 1.1).
        if (_intervencoes.Count == 0)
        {
            MessageBox.Show("Não há intervenções registadas para esta escola — nada para incluir no relatório.",
                "Sem dados para o relatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Guardar histórico de intervenções",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Historico_Intervencoes_{_escola.Nome}_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new RelatorioService(App.Db);
            servico.GerarListaIntervencoes(dialog.FileName,
                idsFiltrados: _intervencoes.Select(i => i.Id).ToList(),
                tituloPersonalizado: $"Histórico de Intervenções — {_escola.Nome}",
                subtituloPersonalizado: "Registo de todas as intervenções técnicas realizadas nesta escola.",
                resumoPorCategoria: true);

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

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
