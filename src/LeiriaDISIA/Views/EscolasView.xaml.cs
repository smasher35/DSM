using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

public partial class EscolasView : UserControl
{
    private Escola? _selecionada;
    private List<Escola> _todas = new();

    public EscolasView()
    {
        InitializeComponent();
        CmbTipo.ItemsSource = Enum.GetValues<TipoEscola>();
        CmbEstado.ItemsSource = App.Db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.EstadoEscola && v.Ativo)
            .OrderBy(v => v.Ordem)
            .Select(v => v.Valor)
            .ToList();
        CarregarCombosEGrid();
    }

    private void CarregarCombosEGrid()
    {
        var agrupamentos = App.Db.Agrupamentos.OrderBy(a => a.Nome).ToList();

        CmbAgrupamento.ItemsSource = agrupamentos;

        var comFiltroTodos = new List<Agrupamento> { new() { Id = 0, Nome = "(Todos os agrupamentos)" } };
        comFiltroTodos.AddRange(agrupamentos);
        CmbFiltroAgrupamento.ItemsSource = comFiltroTodos;
        CmbFiltroAgrupamento.SelectedIndex = 0;

        RecarregarGrid();
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

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionada = Grid.SelectedItem as Escola;
        if (_selecionada == null) return;

        TxtCodEscola.Text = _selecionada.CodEscola;
        TxtCodDgrhe.Text = _selecionada.CodDGRHE?.ToString();
        TxtCodGepe.Text = _selecionada.CodGEPE?.ToString();
        TxtNome.Text = _selecionada.Nome;
        TxtNomeAlternativo.Text = _selecionada.NomeAlternativo;
        TxtMorada.Text = _selecionada.Morada;
        TxtLocalidade.Text = _selecionada.Localidade;
        TxtFreguesia.Text = _selecionada.Freguesia;
        CmbAgrupamento.SelectedValue = _selecionada.AgrupamentoId;
        CmbAgrupamento.SelectedItem = App.Db.Agrupamentos.FirstOrDefault(a => a.Id == _selecionada.AgrupamentoId);
        CmbTipo.SelectedItem = _selecionada.Tipo;
        TxtNumAlunos.Text = _selecionada.NumeroAlunos?.ToString();
        TxtNumSalas.Text = _selecionada.NumeroSalas?.ToString();
        ChkFibra.IsChecked = _selecionada.TemInternetFibra;
        ChkCCTV.IsChecked = _selecionada.TemCCTV;
        ChkVPN.IsChecked = _selecionada.TemVPN;
        ChkBiblioteca.IsChecked = _selecionada.TemBiblioteca;
        CmbEstado.SelectedItem = _selecionada.Estado;
        TxtObservacoes.Text = _selecionada.Observacoes;
    }

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        _selecionada = null;
        Grid.SelectedItem = null;
        foreach (var tb in new[] { TxtCodDgrhe, TxtCodGepe, TxtNome, TxtNomeAlternativo,
                     TxtMorada, TxtLocalidade, TxtFreguesia, TxtNumAlunos, TxtNumSalas, TxtObservacoes })
            tb.Clear();
        TxtCodEscola.Text = "(atribuído automaticamente ao gravar)";
        CmbAgrupamento.SelectedItem = null;
        CmbTipo.SelectedItem = TipoEscola.EB1;
        ChkFibra.IsChecked = false;
        ChkCCTV.IsChecked = false;
        ChkVPN.IsChecked = false;
        ChkBiblioteca.IsChecked = false;
        CmbEstado.SelectedItem = EstadosEscola.Ativa;
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtNome.Text) || CmbAgrupamento.SelectedItem is not Agrupamento agrupamento)
        {
            MessageBox.Show("Indique pelo menos o nome da escola e o agrupamento.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Verifica duplicados por semelhança de nome (exceto a própria escola em edição)
        var possivelDuplicado = _todas.FirstOrDefault(e =>
            (_selecionada == null || e.Id != _selecionada.Id) &&
            TextNormalizer.AreLikelySameSchool(e.Nome, TxtNome.Text));

        if (possivelDuplicado != null)
        {
            var continuar = MessageBox.Show(
                $"Já existe uma escola com nome muito semelhante: '{possivelDuplicado.Nome}'.\n" +
                "Pode tratar-se da mesma escola com nome diferente.\n\n" +
                "Deseja continuar e guardar mesmo assim?",
                "Possível escola duplicada", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (continuar != MessageBoxResult.Yes) return;
        }

        int? codDgrhe = int.TryParse(TxtCodDgrhe.Text, out var d) ? d : null;
        int? codGepe = int.TryParse(TxtCodGepe.Text, out var g) ? g : null;
        int? numAlunos = int.TryParse(TxtNumAlunos.Text, out var na) ? na : null;
        int? numSalas = int.TryParse(TxtNumSalas.Text, out var ns) ? ns : null;

        if (_selecionada == null)
        {
            _selecionada = new Escola { CodEscola = CodigoEscolaService.ProximoCodigo(App.Db, CmbTipo.SelectedItem as string) };
            App.Db.Escolas.Add(_selecionada);
        }

        _selecionada.CodDGRHE = codDgrhe;
        _selecionada.CodGEPE = codGepe;
        _selecionada.Nome = TxtNome.Text.Trim();
        _selecionada.NomeAlternativo = string.IsNullOrWhiteSpace(TxtNomeAlternativo.Text) ? null : TxtNomeAlternativo.Text.Trim();
        _selecionada.Morada = TxtMorada.Text;
        _selecionada.Localidade = TxtLocalidade.Text;
        _selecionada.Freguesia = TxtFreguesia.Text;
        _selecionada.AgrupamentoId = agrupamento.Id;
        _selecionada.Tipo = CmbTipo.SelectedItem?.ToString() ?? "EB1";
        _selecionada.NumeroAlunos = numAlunos;
        _selecionada.NumeroSalas = numSalas;
        _selecionada.TemInternetFibra = ChkFibra.IsChecked == true;
        _selecionada.TemCCTV = ChkCCTV.IsChecked == true;
        _selecionada.TemVPN = ChkVPN.IsChecked == true;
        _selecionada.TemBiblioteca = ChkBiblioteca.IsChecked == true;
        _selecionada.Estado = CmbEstado.SelectedItem as string ?? EstadosEscola.Ativa;
        _selecionada.Observacoes = TxtObservacoes.Text;

        App.Db.SaveChanges();
        RecarregarGrid();
        Novo_Click(sender, e);
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionada == null) return;

        var temHistorico = App.Db.Intervencoes.Any(i => i.EscolaId == _selecionada.Id) ||
                            App.Db.PedidosIntervencao.Any(p => p.EscolaId == _selecionada.Id);
        if (temHistorico)
        {
            MessageBox.Show(
                "Esta escola tem intervenções ou pedidos associados. Considere desativá-la em vez de eliminar.",
                "Não permitido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Eliminar a escola '{_selecionada.Nome}'?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        App.Db.Escolas.Remove(_selecionada);
        App.Db.SaveChanges();
        RecarregarGrid();
        Novo_Click(sender, e);
    }
}
