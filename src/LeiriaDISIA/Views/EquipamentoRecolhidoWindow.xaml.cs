using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
// O projeto tem UseWindowsForms=true (para o seletor de cor noutro módulo — ver
// Services/ColorPickerHelper.cs), o que torna estes tipos ambíguos entre WPF e WinForms sempre que
// ambos os namespaces ficam disponíveis no mesmo ficheiro. Aliases explícitos resolvem a ambiguidade
// sem ter de qualificar o nome completo em cada utilização.
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using ComboBox = System.Windows.Controls.ComboBox;
// CheckBox já tem alias global em GlobalUsings.cs — não repetir aqui (senão dá CS1537).
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

/// <summary>
/// Módulo de Equipamento Recolhido: equipamento que existe no inventário e foi levado para as
/// instalações da DISIA para ser intervencionado. É uma mudança temporária de local — o
/// equipamento continua ativo e a contar para os totais normais, não é abatido nem desativado.
/// Por omissão mostra apenas o que ainda não foi entregue; o histórico completo (incluindo já
/// entregues) fica disponível através da caixa "Mostrar histórico".
/// </summary>
public partial class EquipamentoRecolhidoWindow : Window
{
    private List<EquipamentoRecolhido> _todos = new();

    public EquipamentoRecolhidoWindow()
    {
        InitializeComponent();
        // Perfil Guest (Services/SessaoAtual.PodeEditar): acesso só de leitura a este módulo -
        // ver Services/PermissoesService.cs.
        LeiriaDISIA.Services.PermissoesService.AplicarSomenteLeituraSeGuest(BtnInserir);

        // Legenda do badge de Estado (item 5.2). Estado é texto livre (não enum), tal como
        // acontece com o Estado de Equipamento — por isso itera-se sobre os 4 valores fixos em
        // vez de Enum.GetValues, seguindo o mesmo raciocínio usado para esses outros módulos.
        LegendaEstados.ItemsSource = new[]
        {
            EstadosRecolha.Pendente, EstadosRecolha.EmReparacao, EstadosRecolha.AguardaEntrega, EstadosRecolha.Entregue
        }.Select(estado => new { Nome = estado, Cor = EstadoCores.CorEstadoRecolha(estado) }).ToList();

        Recarregar();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Filtro_TextChanged(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void ChkHistorico_Changed(object sender, RoutedEventArgs e) => Recarregar();

    private void Recarregar()
    {
        var query = App.Db.EquipamentosRecolhidos.Include(r => r.Equipamento).ThenInclude(eq => eq!.Escola)
            .AsQueryable();

        if (ChkHistorico.IsChecked != true)
            query = query.Where(r => r.DataEntrega == null);

        _todos = query.OrderByDescending(r => r.DataRecolha).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        if (TxtPesquisa == null) return;

        IEnumerable<EquipamentoRecolhido> resultado = _todos;

        var termo = TxtPesquisa?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            resultado = resultado.Where(r =>
                (r.Equipamento != null && r.Equipamento.NumeroSerie.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (r.Equipamento?.Escola != null && r.Equipamento.Escola.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                r.Estado.Contains(termo, StringComparison.OrdinalIgnoreCase));
        }

        Grid.ItemsSource = resultado.ToList();
    }

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new EquipamentoRecolhidoEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Editar(EquipamentoRecolhido registo)
    {
        var registoAtual = App.Db.EquipamentosRecolhidos.First(r => r.Id == registo.Id);

        var janela = new EquipamentoRecolhidoEditWindow(registoAtual) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    // Item 5.1: edição por duplo clique na linha (em vez do antigo botão "Editar" dedicado),
    // seguindo o comportamento já normalizado nos restantes módulos. A linha tem também um botão
    // "Entregar na Escola" embutido — se o duplo clique começar nesse botão (ou nalgum outro
    // controlo interativo dentro da grelha), não deve abrir a edição por cima, senão o clique no
    // botão deixaria de funcionar como esperado.
    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OrigemEstaSobreControloInterativo(e.OriginalSource as DependencyObject)) return;
        if (Grid.SelectedItem is not EquipamentoRecolhido registo) return;

        Editar(registo);
    }

    /// <summary>Percorre a árvore visual a partir do elemento onde o clique ocorreu, à procura de
    /// um botão ou outro controlo interativo, para não abrir a edição por cima do clique nesse
    /// controlo (ex.: o botão "Entregar na Escola" desta grid).</summary>
    private static bool OrigemEstaSobreControloInterativo(DependencyObject? origem)
    {
        while (origem != null && origem is not DataGridRow)
        {
            if (origem is ButtonBase or TextBoxBase or ComboBox or CheckBox) return true;
            origem = System.Windows.Media.VisualTreeHelper.GetParent(origem);
        }
        return false;
    }

    private void Entregar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id }) return;

        var registo = App.Db.EquipamentosRecolhidos.Include(r => r.Equipamento).First(r => r.Id == id);
        if (!registo.PodeSerEntregue)
        {
            MessageBox.Show("Só é possível entregar equipamento depois de a Atividade DISIA de reparação ser fechada (estado 'Aguarda Entrega').",
                "Entrega não permitida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmar = MessageBox.Show(
            $"Confirma a entrega do equipamento (Nº Série {registo.Equipamento?.NumeroSerie}) de volta à escola?",
            "Confirmar entrega", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmar != MessageBoxResult.Yes) return;

        registo.Estado = EstadosRecolha.Entregue;
        registo.DataEntrega = DateTime.Today;
        if (registo.Equipamento != null) registo.Equipamento.Estado = EstadosEquipamento.EmServico;
        App.Db.SaveChanges();
        Recarregar();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de equipamento recolhido",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Equipamento_Recolhido_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaEquipamentoRecolhido(dialog.FileName);

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
