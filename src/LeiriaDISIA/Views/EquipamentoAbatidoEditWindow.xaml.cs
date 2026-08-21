using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class EquipamentoAbatidoEditWindow : Window
{
    private readonly EquipamentoAbatido? _existente;
    private static readonly string[] StatusComuns = { "Abatido", "Cancelado", "Doado", "Em processo de abate", "Reciclado" };

    private Equipamento? _equipamentoSelecionado;
    private List<Escola> _escolasComPlaceholder = new();

    public bool Sucesso { get; private set; }

    public EquipamentoAbatidoEditWindow(EquipamentoAbatido? abate)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _existente = abate;

        var statusConfigurados = App.Db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.StatusAbate && v.Ativo)
            .OrderBy(v => v.Valor)
            .Select(v => v.Valor)
            .ToArray();
        CmbStatus.ItemsSource = statusConfigurados.Length > 0 ? statusConfigurados : StatusComuns;

        // Escola em primeiro lugar: ao escolher a escola, o botão "Escolher..." abre o
        // equipamento já filtrado/priorizado para essa escola, reduzindo o número de
        // equipamentos a procurar (mesma lógica usada no Equipamento Recolhido).
        var escolasDisponiveis = new List<Escola> { new() { Id = 0, Nome = "(Todas as escolas / não aplicável)" } };
        escolasDisponiveis.AddRange(App.Db.Escolas.OrderBy(e => e.Nome));
        _escolasComPlaceholder = escolasDisponiveis;
        CmbEscola.ItemsSource = escolasDisponiveis;

        if (abate == null)
        {
            TxtTitulo.Text = "Registar Abate";
            CmbEscola.SelectedIndex = 0;
            DpAbate.SelectedDate = DateTime.Today;
            CmbStatus.Text = "Abatido";
            return;
        }

        TxtTitulo.Text = "Editar Registo de Abate";

        var completo = App.Db.EquipamentosAbatidos.Include(a => a.Equipamento).ThenInclude(eq => eq!.Escola)
            .First(a => a.Id == abate.Id);

        _equipamentoSelecionado = completo.Equipamento;
        AtualizarTextoEquipamento();
        CmbEscola.SelectedItem = escolasDisponiveis.FirstOrDefault(e => e.Id == (_equipamentoSelecionado?.EscolaId ?? 0))
                                 ?? escolasDisponiveis[0];
        TxtEscolaOuLocal.Text = abate.EscolaOuLocal;
        TxtDescricao.Text = abate.DescricaoEquipamento;
        TxtNumeroSerie.Text = abate.NumeroSerie;
        TxtNumeroInventario.Text = abate.NumeroInventario;
        DpAbate.SelectedDate = abate.DataAbate;
        CmbStatus.Text = abate.Status;
        TxtObservacoes.Text = abate.Observacoes;
    }

    private void AtualizarTextoEquipamento()
    {
        TxtEquipamentoSelecionado.Text = _equipamentoSelecionado == null
            ? ""
            : $"{_equipamentoSelecionado.NumeroSerie} — {_equipamentoSelecionado.Tipo} {_equipamentoSelecionado.Marca} {_equipamentoSelecionado.Modelo} " +
              $"({_equipamentoSelecionado.Escola?.Nome ?? "sem escola"})";
    }

    private void CmbEscola_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Mudar a escola não desfaz automaticamente o equipamento já escolhido; serve apenas
        // para filtrar/priorizar a lista que o botão "Escolher..." vai mostrar a seguir.
    }

    /// <summary>8: campo de busca antes da dropdown de escola, tal como já usado noutros módulos —
    /// com muitas escolas, filtrar por nome/localidade/código GEPE facilita a seleção. O item
    /// "(Todas as escolas / não aplicável)" mantém-se sempre visível, independentemente do termo.</summary>
    private void TxtPesquisaEscola_TextChanged(object sender, TextChangedEventArgs e)
    {
        var termo = TxtPesquisaEscola.Text.Trim();
        var placeholder = _escolasComPlaceholder[0];
        var filtradas = string.IsNullOrWhiteSpace(termo)
            ? _escolasComPlaceholder
            : new[] { placeholder }.Concat(_escolasComPlaceholder.Skip(1).Where(esc =>
                esc.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                (esc.Localidade ?? "").Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                (esc.CodGEPE?.ToString() ?? "").Contains(termo, StringComparison.OrdinalIgnoreCase))).ToList();

        var selecionadaAtual = CmbEscola.SelectedItem as Escola;
        CmbEscola.ItemsSource = filtradas;
        CmbEscola.SelectedItem = selecionadaAtual != null && filtradas.Contains(selecionadaAtual)
            ? selecionadaAtual : placeholder;
    }

    private void EscolherEquipamento_Click(object sender, RoutedEventArgs e)
    {
        var escolaSelecionada = CmbEscola.SelectedItem as Escola;
        var escolaIdFiltro = escolaSelecionada is { Id: not 0 } ? escolaSelecionada.Id : (int?)null;

        var picker = new EquipamentoPickerWindow(escolaIdFiltro) { Owner = this };
        if (picker.ShowDialog() != true || picker.EquipamentoSelecionado == null) return;

        _equipamentoSelecionado = App.Db.Equipamentos.Include(eq => eq.Escola)
            .First(eq => eq.Id == picker.EquipamentoSelecionado.Id);
        AtualizarTextoEquipamento();

        // Preenche automaticamente os campos a partir do equipamento escolhido (o utilizador
        // pode sempre corrigir manualmente a seguir).
        TxtDescricao.Text = $"{_equipamentoSelecionado.Tipo} {_equipamentoSelecionado.Marca} {_equipamentoSelecionado.Modelo}".Trim();
        TxtNumeroSerie.Text = _equipamentoSelecionado.NumeroSerie;
        TxtNumeroInventario.Text = _equipamentoSelecionado.NumeroInventario;
        TxtEscolaOuLocal.Text = _equipamentoSelecionado.Escola?.Nome ?? _equipamentoSelecionado.LocalNaoEscolar;

        if (_equipamentoSelecionado.EscolaId != null)
        {
            var escolaDoEquipamento = _escolasComPlaceholder
                .FirstOrDefault(esc => esc.Id == _equipamentoSelecionado.EscolaId);
            if (escolaDoEquipamento != null)
            {
                TxtPesquisaEscola.Text = "";
                CmbEscola.ItemsSource = _escolasComPlaceholder;
                CmbEscola.SelectedItem = escolaDoEquipamento;
            }
        }
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDescricao.Text))
        {
            MessageBox.Show("Descreva o equipamento a abater.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtNumeroSerie.Text) && string.IsNullOrWhiteSpace(TxtNumeroInventario.Text))
        {
            MessageBox.Show("Indique o número de série ou o número de inventário do equipamento a abater.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DpAbate.SelectedDate == null)
        {
            MessageBox.Show("Indique a data de abate.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtEscolaOuLocal.Text))
        {
            MessageBox.Show("Indique a escola/local do equipamento a abater.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var equipamentoId = _equipamentoSelecionado?.Id;

        EquipamentoAbatido abate;
        if (_existente == null)
        {
            abate = new EquipamentoAbatido();
            App.Db.EquipamentosAbatidos.Add(abate);
        }
        else
        {
            abate = App.Db.EquipamentosAbatidos.First(a => a.Id == _existente.Id);
        }

        abate.EquipamentoId = equipamentoId;
        abate.EscolaOuLocal = TxtEscolaOuLocal.Text;
        abate.DescricaoEquipamento = TxtDescricao.Text.Trim();
        abate.NumeroSerie = string.IsNullOrWhiteSpace(TxtNumeroSerie.Text) ? null : TxtNumeroSerie.Text.Trim();
        abate.NumeroInventario = string.IsNullOrWhiteSpace(TxtNumeroInventario.Text) ? null : TxtNumeroInventario.Text.Trim();
        abate.DataAbate = DpAbate.SelectedDate ?? DateTime.Today;
        abate.Status = CmbStatus.Text;
        abate.Observacoes = TxtObservacoes.Text;

        App.Db.SaveChanges();

        if (equipamentoId != null)
        {
            var equipamento = App.Db.Equipamentos.First(x => x.Id == equipamentoId);
            equipamento.Estado = EstadosEquipamento.Abatido;
            App.Db.SaveChanges();
        }

        Sucesso = true;
        Close();
    }
}
