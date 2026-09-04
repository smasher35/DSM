using System.Windows;
using System.Windows.Controls;
// O projeto tem UseWindowsForms=true (para o seletor de cor noutro módulo — ver
// Services/ColorPickerHelper.cs), o que torna estes tipos ambíguos entre WPF e WinForms sempre que
// ambos os namespaces ficam disponíveis no mesmo ficheiro. Aliases explícitos resolvem a ambiguidade
// sem ter de qualificar o nome completo em cada utilização — mesmo padrão já usado em
// Views/EquipamentoRecolhidoWindow.xaml.cs.
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

/// <summary>
/// Item 3.1: o mesmo tipo de pesquisa avançada do item 2.1 (ver
/// <see cref="PesquisaAvancadaEquipamentoWindow"/>), aplicado às Atividades DISIA em vez de
/// Equipamento — mesma lógica de filtros (campo + comparador + valor, combinados com E), mesmo
/// motor de comparação partilhado (<see cref="PesquisaAvancadaService"/>), sem características
/// adicionais (Atividade DISIA não tem sistema de características EAV como o Equipamento).
/// </summary>
public partial class PesquisaAvancadaAtividadeDisiaWindow : Window
{
    private readonly List<AtividadeDisia> _todasAtividades;
    private readonly List<CampoPesquisavel<AtividadeDisia>> _campos;
    private readonly List<LinhaFiltroUi> _linhas = new();
    private List<AtividadeDisia> _resultado = new();

    private class LinhaFiltroUi
    {
        public StackPanel Painel = null!;
        public ComboBox CmbCampo = null!;
        public ComboBox CmbComparador = null!;
        public TextBox TxtValor = null!;
    }

    private class LinhaResultado
    {
        public DateTime Data { get; set; }
        public string Categoria { get; set; } = "";
        public string Local { get; set; } = "";
        public string Descricao { get; set; } = "";
        public int Quantidade { get; set; }
        public string Estado { get; set; } = "";
    }

    public PesquisaAvancadaAtividadeDisiaWindow()
    {
        InitializeComponent();

        _todasAtividades = App.Db.AtividadesDisia.Include(a => a.Categoria).ToList();
        _campos = PesquisaAvancadaService.ObterCamposAtividadeDisia();

        AdicionarLinhaFiltro();
    }

    private void AdicionarFiltro_Click(object sender, RoutedEventArgs e) => AdicionarLinhaFiltro();

    private void AdicionarLinhaFiltro()
    {
        var cmbCampo = new ComboBox { Width = 240, Margin = new Thickness(0, 0, 8, 0), ItemsSource = _campos };
        var cmbComparador = new ComboBox { Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var txtValor = new TextBox { Width = 170, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var btnRemover = new Button { Content = "✕", Width = 28, ToolTip = "Remover este filtro" };

        var painel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        painel.Children.Add(cmbCampo);
        painel.Children.Add(cmbComparador);
        painel.Children.Add(txtValor);
        painel.Children.Add(btnRemover);

        var linha = new LinhaFiltroUi
        {
            Painel = painel,
            CmbCampo = cmbCampo,
            CmbComparador = cmbComparador,
            TxtValor = txtValor
        };

        cmbCampo.SelectionChanged += (_, _) => AtualizarComparadoresELinha(linha);
        btnRemover.Click += (_, _) =>
        {
            PainelFiltros.Children.Remove(painel);
            _linhas.Remove(linha);
        };

        _linhas.Add(linha);
        PainelFiltros.Children.Add(painel);

        if (_campos.Count > 0) cmbCampo.SelectedIndex = 0;
    }

    /// <summary>Sem campos Booleanos nesta entidade — ao contrário da versão para Equipamento (ver
    /// <see cref="PesquisaAvancadaEquipamentoWindow.AtualizarComparadoresELinha"/>), só é preciso
    /// atualizar a lista de comparadores; a caixa de valor é sempre a mesma caixa de texto.</summary>
    private void AtualizarComparadoresELinha(LinhaFiltroUi linha)
    {
        if (linha.CmbCampo.SelectedItem is not CampoPesquisavel<AtividadeDisia> campo) return;

        var comparadorAnterior = linha.CmbComparador.SelectedItem as string;
        var comparadoresValidos = PesquisaAvancadaService.ComparadoresPorTipo[campo.Tipo];
        linha.CmbComparador.ItemsSource = comparadoresValidos;
        linha.CmbComparador.SelectedItem = comparadorAnterior != null && comparadoresValidos.Contains(comparadorAnterior)
            ? comparadorAnterior
            : comparadoresValidos.FirstOrDefault();

        linha.TxtValor.ToolTip = campo.Tipo switch
        {
            TipoDadoCampoPesquisa.Data => "Formato: AAAA-MM-DD (ex.: 2026-08-15)",
            TipoDadoCampoPesquisa.Numero => "Valor numérico (ex.: 2026, ou 1)",
            _ => null
        };
    }

    private List<FiltroPesquisa<AtividadeDisia>> ObterFiltros() =>
        _linhas.Select(l => new FiltroPesquisa<AtividadeDisia>
        {
            Campo = l.CmbCampo.SelectedItem as CampoPesquisavel<AtividadeDisia>,
            Comparador = l.CmbComparador.SelectedItem as string,
            Valor = l.TxtValor.Text
        }).ToList();

    private void Pesquisar_Click(object sender, RoutedEventArgs e)
    {
        var filtros = ObterFiltros();
        _resultado = PesquisaAvancadaService.Aplicar(_todasAtividades, filtros);

        Grid.ItemsSource = _resultado.Select(a => new LinhaResultado
        {
            Data = a.Data,
            Categoria = a.Categoria?.Nome ?? "",
            Local = a.Local ?? "",
            Descricao = a.Descricao,
            Quantidade = a.Quantidade,
            Estado = a.Estado.ToString()
        }).ToList();

        TxtResultado.Text = _resultado.Count switch
        {
            0 => "Nenhuma atividade corresponde aos filtros indicados.",
            1 => "1 atividade encontrada.",
            _ => $"{_resultado.Count} atividades encontradas."
        };

        BtnGerarPdf.IsEnabled = _resultado.Count > 0;
    }

    private void GerarPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_resultado.Count == 0)
        {
            MessageBox.Show("Pesquise primeiro — não há resultados para incluir no relatório.",
                "Sem dados para o relatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Guardar pesquisa avançada de atividades DISIA",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Pesquisa_Avancada_Atividades_DISIA_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var descricaoFiltros = PesquisaAvancadaService.Descrever(ObterFiltros());
            var servico = new RelatorioService(App.Db);
            servico.GerarListaAtividadesDisia(dialog.FileName, ano: null, idsFiltrados: _resultado.Select(a => a.Id).ToList(),
                tituloPersonalizado: "Pesquisa Avançada de Atividades DISIA",
                subtituloPersonalizado: $"Filtros aplicados: {descricaoFiltros}");

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
