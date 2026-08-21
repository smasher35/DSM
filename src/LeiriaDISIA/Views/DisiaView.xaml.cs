using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

public partial class DisiaView : UserControl
{
    private AtividadeDisia? _selecionada;
    private static readonly string[] NomesMeses =
    {
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    };

    public DisiaView()
    {
        InitializeComponent();

        var anoAtual = DateTime.Today.Year;
        CmbAno.ItemsSource = Enumerable.Range(anoAtual - 3, 6).ToList();
        CmbAno.SelectedItem = anoAtual;

        var meses = new List<string> { "(Todos)" };
        meses.AddRange(NomesMeses);
        CmbMes.ItemsSource = meses;
        CmbMes.SelectedIndex = DateTime.Today.Month;

        CmbCategoria.ItemsSource = App.Db.CategoriasDisia.OrderBy(c => c.Nome).ToList();
        CmbEstado.ItemsSource = Enum.GetValues<EstadoIntervencao>();

        Recarregar();
        Novo_Click(this, new RoutedEventArgs());
    }

    private void Filtro_Changed(object sender, SelectionChangedEventArgs e) => Recarregar();

    private void Recarregar()
    {
        if (CmbAno == null || Grid == null) return;
        var ano = (int?)CmbAno.SelectedItem ?? DateTime.Today.Year;
        var mesIndex = CmbMes.SelectedIndex;

        var query = App.Db.AtividadesDisia.Include(a => a.Categoria).Where(a => a.Ano == ano).AsQueryable();
        if (mesIndex > 0) query = query.Where(a => a.Mes == mesIndex);

        Grid.ItemsSource = query.OrderByDescending(a => a.Data).ToList();
        AtualizarEstatisticas();
    }

    private void AtualizarEstatisticas()
    {
        try
        {
            var ano = (int?)CmbAno.SelectedItem ?? DateTime.Today.Year;
            var mesIndex = CmbMes.SelectedIndex;

            // Total do ano
            var totalAno = App.Db.AtividadesDisia.Where(a => a.Ano == ano).Count();

            // Total do mês (se selecionado)
            var totalMes = mesIndex > 0
                ? App.Db.AtividadesDisia.Where(a => a.Ano == ano && a.Mes == mesIndex).Count()
                : 0;

            // Local mais intervencionado (mês se selecionado, senão ano)
            var localMaisIntervencionado = mesIndex > 0
                ? App.Db.AtividadesDisia
                    .Where(a => a.Ano == ano && a.Mes == mesIndex && !string.IsNullOrEmpty(a.Local))
                    .GroupBy(a => a.Local)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "—"
                : App.Db.AtividadesDisia
                    .Where(a => a.Ano == ano && !string.IsNullOrEmpty(a.Local))
                    .GroupBy(a => a.Local)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "—";

            // Total de quantidade (vezes que o serviço foi prestado)
            var totalQuantidade = mesIndex > 0
                ? App.Db.AtividadesDisia.Where(a => a.Ano == ano && a.Mes == mesIndex).Sum(a => a.Quantidade)
                : App.Db.AtividadesDisia.Where(a => a.Ano == ano).Sum(a => a.Quantidade);

            // Categoria mais utilizada (por frequência)
            var categoriaMaisUtilizada = mesIndex > 0
                ? App.Db.AtividadesDisia
                    .Where(a => a.Ano == ano && a.Mes == mesIndex && a.Categoria != null)
                    .GroupBy(a => a.Categoria.Nome)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "—"
                : App.Db.AtividadesDisia
                    .Where(a => a.Ano == ano && a.Categoria != null)
                    .GroupBy(a => a.Categoria.Nome)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "—";

            // Categoria mais intervencionada (por quantidade total de serviços)
            var categoriaMaisIntervencionada = mesIndex > 0
                ? App.Db.AtividadesDisia
                    .Where(a => a.Ano == ano && a.Mes == mesIndex && a.Categoria != null)
                    .GroupBy(a => a.Categoria.Nome)
                    .OrderByDescending(g => g.Sum(x => x.Quantidade))
                    .FirstOrDefault()?.Key ?? "—"
                : App.Db.AtividadesDisia
                    .Where(a => a.Ano == ano && a.Categoria != null)
                    .GroupBy(a => a.Categoria.Nome)
                    .OrderByDescending(g => g.Sum(x => x.Quantidade))
                    .FirstOrDefault()?.Key ?? "—";

            // Atualizar TextBlocks (agora são campos públicos gerados pelo designer)
            TxtTotalAno.Text = totalAno.ToString();
            TxtTotalMes.Text = totalMes.ToString();
            TxtLocalMaisIntervencionado.Text = localMaisIntervencionado;
            TxtTotalQuantidade.Text = totalQuantidade.ToString();
            TxtCategoriaMaisUtilizada.Text = categoriaMaisUtilizada;
            TxtCategoriaMaisIntervencionada.Text = categoriaMaisIntervencionada;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro em AtualizarEstatisticas: {ex.Message}");
        }
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionada = Grid.SelectedItem as AtividadeDisia;
        if (_selecionada == null) return;

        DpData.SelectedDate = _selecionada.Data;
        TxtDescricao.Text = _selecionada.Descricao;
        CmbCategoria.SelectedItem = ((List<CategoriaDisia>)CmbCategoria.ItemsSource)
            .FirstOrDefault(c => c.Id == _selecionada.CategoriaDisiaId);
        TxtLocal.Text = _selecionada.Local;
        TxtDivisao.Text = _selecionada.Divisao;
        TxtSuporte.Text = _selecionada.Suporte;
        TxtQuantidade.Text = _selecionada.Quantidade.ToString();
        CmbEstado.SelectedItem = _selecionada.Estado;
        TxtObservacoes.Text = _selecionada.Observacoes;
    }

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        _selecionada = null;
        Grid.SelectedItem = null;
        DpData.SelectedDate = DateTime.Today;
        TxtDescricao.Clear();
        CmbCategoria.SelectedItem = null;
        TxtLocal.Clear();
        TxtDivisao.Clear();
        TxtSuporte.Clear();
        TxtQuantidade.Text = "1";
        CmbEstado.SelectedItem = EstadoIntervencao.Fechada;
        TxtObservacoes.Clear();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDescricao.Text))
        {
            MessageBox.Show("Descreva a atividade realizada.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var data = DpData.SelectedDate ?? DateTime.Today;
        int.TryParse(TxtQuantidade.Text, out var quantidade);
        if (quantidade <= 0) quantidade = 1;

        if (_selecionada == null)
        {
            _selecionada = new AtividadeDisia();
            App.Db.AtividadesDisia.Add(_selecionada);
        }

        _selecionada.Data = data;
        _selecionada.Mes = data.Month;
        _selecionada.Ano = data.Year;
        _selecionada.Descricao = TxtDescricao.Text.Trim();
        _selecionada.CategoriaDisiaId = (CmbCategoria.SelectedItem as CategoriaDisia)?.Id;
        _selecionada.Local = TxtLocal.Text;
        _selecionada.Divisao = TxtDivisao.Text;
        _selecionada.Suporte = TxtSuporte.Text;
        _selecionada.Quantidade = quantidade;
        _selecionada.Estado = (EstadoIntervencao)(CmbEstado.SelectedItem ?? EstadoIntervencao.Fechada);
        _selecionada.Observacoes = TxtObservacoes.Text;

        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionada == null) return;
        if (MessageBox.Show("Eliminar esta atividade?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        App.Db.AtividadesDisia.Remove(_selecionada);
        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }
}
