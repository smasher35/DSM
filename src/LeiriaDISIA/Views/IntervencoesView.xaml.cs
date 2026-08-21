using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

public partial class IntervencoesView : UserControl
{
    private Intervencao? _selecionada;
    private readonly List<CheckBox> _checkBoxesCategorias = new();
    private static readonly string[] NomesMeses =
    {
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    };

    public IntervencoesView()
    {
        InitializeComponent();

        var anoAtual = DateTime.Today.Year;
        CmbAno.ItemsSource = Enumerable.Range(anoAtual - 3, 6).ToList();
        CmbAno.SelectedItem = anoAtual;

        var meses = new List<string> { "(Todos)" };
        meses.AddRange(NomesMeses);
        CmbMes.ItemsSource = meses;
        CmbMes.SelectedIndex = DateTime.Today.Month; // 0=Todos, 1=Jan...

        var agrupamentos = new List<Agrupamento> { new() { Id = 0, Nome = "(Todos)" } };
        agrupamentos.AddRange(App.Db.Agrupamentos.OrderBy(a => a.Nome));
        CmbAgrupamentoFiltro.ItemsSource = agrupamentos;
        CmbAgrupamentoFiltro.SelectedIndex = 0;

        CmbEscola.ItemsSource = App.Db.Escolas.Include(e => e.Agrupamento).OrderBy(e => e.Nome).ToList();
        CmbEstado.ItemsSource = Enum.GetValues<EstadoIntervencao>();

        foreach (var cat in App.Db.CategoriasIntervencao.Where(c => c.Ativa).OrderBy(c => c.Nome))
        {
            var cb = new CheckBox { Content = cat.Nome, Tag = cat, Margin = new Thickness(0, 2, 0, 2) };
            _checkBoxesCategorias.Add(cb);
            ListaCategorias.Items.Add(cb);
        }

        Recarregar();
        Novo_Click(this, new RoutedEventArgs());
    }

    private void Filtro_Changed(object sender, SelectionChangedEventArgs e) => Recarregar();

    private void Recarregar()
    {
        if (CmbAno == null || Grid == null) return;

        var ano = (int?)CmbAno.SelectedItem ?? DateTime.Today.Year;
        var mesIndex = CmbMes.SelectedIndex; // 0 = todos, 1..12 = mês
        var agrupamentoSel = CmbAgrupamentoFiltro.SelectedItem as Agrupamento;

        var query = App.Db.Intervencoes
            .Include(i => i.Escola)
            .Include(i => i.Agrupamento)
            .Where(i => i.Ano == ano)
            .AsQueryable();

        if (mesIndex > 0)
            query = query.Where(i => i.Mes == mesIndex);

        if (agrupamentoSel != null && agrupamentoSel.Id != 0)
            query = query.Where(i => i.AgrupamentoId == agrupamentoSel.Id);

        Grid.ItemsSource = query.OrderByDescending(i => i.Data).ToList();
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionada = Grid.SelectedItem as Intervencao;
        if (_selecionada == null) return;

        var completa = App.Db.Intervencoes.Include(i => i.Categorias).First(i => i.Id == _selecionada.Id);

        DpData.SelectedDate = completa.Data;
        CmbEscola.SelectedItem = ((List<Escola>)CmbEscola.ItemsSource).FirstOrDefault(x => x.Id == completa.EscolaId);
        TxtDescricao.Text = completa.Descricao;
        TxtMaterial.Text = completa.MaterialRecolhidoAbatido;
        CmbEstado.SelectedItem = completa.Estado;
        TxtMotivoPendente.Text = completa.MotivoPendente;

        var idsCategorias = completa.Categorias.Select(c => c.CategoriaIntervencaoId).ToHashSet();
        foreach (var cb in _checkBoxesCategorias)
            cb.IsChecked = cb.Tag is CategoriaIntervencao cat && idsCategorias.Contains(cat.Id);
    }

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        _selecionada = null;
        Grid.SelectedItem = null;
        DpData.SelectedDate = DateTime.Today;
        CmbEscola.SelectedItem = null;
        TxtDescricao.Clear();
        TxtMaterial.Clear();
        CmbEstado.SelectedItem = EstadoIntervencao.Fechada;
        TxtMotivoPendente.Clear();
        foreach (var cb in _checkBoxesCategorias) cb.IsChecked = false;
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (CmbEscola.SelectedItem is not Escola escola || string.IsNullOrWhiteSpace(TxtDescricao.Text))
        {
            MessageBox.Show("Selecione a escola e descreva a intervenção.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var data = DpData.SelectedDate ?? DateTime.Today;
        var estado = (EstadoIntervencao)(CmbEstado.SelectedItem ?? EstadoIntervencao.Fechada);

        Intervencao intervencao;
        if (_selecionada == null)
        {
            intervencao = new Intervencao();
            App.Db.Intervencoes.Add(intervencao);
        }
        else
        {
            intervencao = App.Db.Intervencoes.Include(i => i.Categorias).First(i => i.Id == _selecionada.Id);
            App.Db.IntervencaoCategorias.RemoveRange(intervencao.Categorias);
        }

        intervencao.Data = data;
        intervencao.Mes = data.Month;
        intervencao.Ano = data.Year;
        intervencao.EscolaId = escola.Id;
        intervencao.AgrupamentoId = escola.AgrupamentoId;
        intervencao.Descricao = TxtDescricao.Text.Trim();
        intervencao.MaterialRecolhidoAbatido = string.IsNullOrWhiteSpace(TxtMaterial.Text) ? null : TxtMaterial.Text;
        intervencao.Estado = estado;
        intervencao.MotivoPendente = estado == EstadoIntervencao.Pendente ? TxtMotivoPendente.Text : null;

        foreach (var cb in _checkBoxesCategorias)
        {
            if (cb.IsChecked == true && cb.Tag is CategoriaIntervencao cat)
            {
                intervencao.Categorias.Add(new IntervencaoCategoria
                {
                    CategoriaIntervencaoId = cat.Id,
                    Quantidade = 1
                });
            }
        }

        App.Db.SaveChanges();

        // Se esta intervenção estiver ligada a um pedido, mantém a coerência de estados
        var pedidoLigado = App.Db.PedidosIntervencao.FirstOrDefault(p => p.IntervencaoId == intervencao.Id);
        if (pedidoLigado != null && estado == EstadoIntervencao.Fechada)
        {
            pedidoLigado.Estado = EstadoPedido.Concluido;
            pedidoLigado.DataConclusao ??= DateTime.Today;
            App.Db.SaveChanges();
        }

        Recarregar();
        Novo_Click(sender, e);
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionada == null) return;
        if (MessageBox.Show("Eliminar esta intervenção?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var completa = App.Db.Intervencoes.Include(i => i.Categorias).First(i => i.Id == _selecionada.Id);
        App.Db.IntervencaoCategorias.RemoveRange(completa.Categorias);
        App.Db.Intervencoes.Remove(completa);
        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }
}
