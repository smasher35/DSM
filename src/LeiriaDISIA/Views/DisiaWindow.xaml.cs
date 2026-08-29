using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class DisiaWindow : Window
{
    private static readonly string[] NomesMeses =
    {
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    };

    public DisiaWindow()
    {
        InitializeComponent();

        var anoAtual = DateTime.Today.Year;
        CmbAno.ItemsSource = Enumerable.Range(anoAtual - 3, 6).ToList();
        CmbAno.SelectedItem = anoAtual;

        var meses = new List<string> { "(Todos)" };
        meses.AddRange(NomesMeses);
        CmbMes.ItemsSource = meses;
        CmbMes.SelectedIndex = DateTime.Today.Month;

        // (5.1) Legenda dos quadrados de cor da coluna "Categoria"
        LegendaCategorias.ItemsSource = App.Db.CategoriasDisia.OrderBy(c => c.Nome).ToList();

        // Agendar carregamento após a UI estar pronta
        Dispatcher.InvokeAsync(Recarregar);
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Filtro_Changed(object sender, SelectionChangedEventArgs e) => Recarregar();
    private void Filtro_TextChanged(object sender, TextChangedEventArgs e) => Recarregar();

    private void Recarregar()
    {
        try
        {
            if (CmbAno == null || Grid == null) return;
            var ano = (int?)CmbAno.SelectedItem ?? DateTime.Today.Year;
            var mesIndex = CmbMes.SelectedIndex;

            var query = App.Db.AtividadesDisia.Include(a => a.Categoria).Where(a => a.Ano == ano).AsQueryable();
            if (mesIndex > 0) query = query.Where(a => a.Mes == mesIndex);

            // A partir daqui a pesquisa por texto livre é feita em memória (LINQ-to-Objects), não
            // na base de dados: o SQLite/EF Core não consegue traduzir para SQL a sobrecarga
            // "string.Contains(texto, StringComparison)" usada abaixo (dava erro "could not be
            // translated" ao pesquisar) — juntar ".ToList()" aqui, logo a seguir aos filtros que a
            // base de dados já sabe traduzir (Ano/Mês), resolve isso sem perder a pesquisa
            // insensível a maiúsculas/minúsculas.
            IEnumerable<AtividadeDisia> resultado = query.ToList();

            // Aplicar pesquisa
            var termo = TxtPesquisa?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(termo))
            {
                resultado = resultado.Where(a =>
                    (a.Descricao != null && a.Descricao.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                    (a.Local != null && a.Local.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                    (a.Categoria != null && a.Categoria.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)));
            }

            Grid.ItemsSource = resultado.OrderByDescending(a => a.Data).ToList();
            AtualizarEstatisticas();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro em Recarregar: {ex}");
            MessageBox.Show($"Erro ao carregar dados:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AtualizarEstatisticas()
    {
        try
        {
            var ano = (int?)CmbAno.SelectedItem ?? DateTime.Today.Year;
            var mesIndex = CmbMes.SelectedIndex;

            // Total do ano
            var totalAno = App.Db.AtividadesDisia.Where(a => a.Ano == ano).Count();
            System.Diagnostics.Debug.WriteLine($"Total Ano ({ano}): {totalAno}");

            // Total do mês (se selecionado)
            var totalMes = mesIndex > 0
                ? App.Db.AtividadesDisia.Where(a => a.Ano == ano && a.Mes == mesIndex).Count()
                : 0;
            System.Diagnostics.Debug.WriteLine($"Total Mês ({mesIndex}): {totalMes}");

            // Local mais intervencionado (executar no servidor, depois agrupar em memória).
            // O total mostrado é o mesmo já usado para o escolher (nº de atividades nesse local) -
            // mesma lógica do cartão análogo "Escola mais intervencionada" no Dashboard (ver
            // DashboardService.cs / DashboardView.xaml), agora uniformizada aqui também.
            var localMaisIntervencionadoGrupo = mesIndex > 0
                ? App.Db.AtividadesDisia
                    .Where(a => a.Ano == ano && a.Mes == mesIndex && !string.IsNullOrEmpty(a.Local))
                    .AsEnumerable()  // Traz dados para memória
                    .GroupBy(a => a.Local)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()
                : App.Db.AtividadesDisia
                    .Where(a => a.Ano == ano && !string.IsNullOrEmpty(a.Local))
                    .AsEnumerable()  // Traz dados para memória
                    .GroupBy(a => a.Local)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
            var localMaisIntervencionado = localMaisIntervencionadoGrupo?.Key ?? "—";
            var localMaisIntervencionadoTotal = localMaisIntervencionadoGrupo?.Count() ?? 0;
            System.Diagnostics.Debug.WriteLine($"Local mais intervencionado: {localMaisIntervencionado}");

            // Total de quantidade (vezes que o serviço foi prestado)
            var totalQuantidade = mesIndex > 0
                ? App.Db.AtividadesDisia.Where(a => a.Ano == ano && a.Mes == mesIndex).Sum(a => a.Quantidade)
                : App.Db.AtividadesDisia.Where(a => a.Ano == ano).Sum(a => a.Quantidade);
            System.Diagnostics.Debug.WriteLine($"Total Quantidade: {totalQuantidade}");

            // Categoria mais utilizada (por frequência) - executar no servidor, depois agrupar em memória
            var categoriaMaisUtilizadaGrupo = mesIndex > 0
                ? App.Db.AtividadesDisia
                    .Where(a => a.Ano == ano && a.Mes == mesIndex && a.Categoria != null)
                    .Include(a => a.Categoria)
                    .AsEnumerable()  // Traz dados para memória
                    .GroupBy(a => a.Categoria.Nome)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()
                : App.Db.AtividadesDisia
                    .Where(a => a.Ano == ano && a.Categoria != null)
                    .Include(a => a.Categoria)
                    .AsEnumerable()  // Traz dados para memória
                    .GroupBy(a => a.Categoria.Nome)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
            var categoriaMaisUtilizada = categoriaMaisUtilizadaGrupo?.Key ?? "—";
            var categoriaMaisUtilizadaTotal = categoriaMaisUtilizadaGrupo?.Count() ?? 0;
            System.Diagnostics.Debug.WriteLine($"Categoria mais utilizada: {categoriaMaisUtilizada}");

            // Atividades DISIA pendentes (todas, de qualquer ano/mês - não fica limitado aos filtros atuais,
            // pois o objetivo é mostrar sempre o total de pendências, incluindo as de meses anteriores)
            var atividadesPendentes = App.Db.AtividadesDisia
                .Count(a => a.Estado != EstadoIntervencao.Fechada && a.Estado != EstadoIntervencao.Cancelada);
            System.Diagnostics.Debug.WriteLine($"Atividades DISIA pendentes: {atividadesPendentes}");

            // Atualizar TextBlocks com segurança
            if (TxtTotalAno != null)
            {
                TxtTotalAno.Text = totalAno.ToString();
                System.Diagnostics.Debug.WriteLine("TxtTotalAno atualizado");
            }
            else
                System.Diagnostics.Debug.WriteLine("ERRO: TxtTotalAno é null");

            if (TxtTotalMes != null)
            {
                TxtTotalMes.Text = totalMes.ToString();
                System.Diagnostics.Debug.WriteLine("TxtTotalMes atualizado");
            }
            else
                System.Diagnostics.Debug.WriteLine("ERRO: TxtTotalMes é null");

            if (TxtLocalMaisIntervencionado != null)
            {
                TxtLocalMaisIntervencionado.Text = localMaisIntervencionado;
                if (TxtLocalMaisIntervencionadoValor != null) TxtLocalMaisIntervencionadoValor.Text = localMaisIntervencionadoTotal.ToString();
                System.Diagnostics.Debug.WriteLine("TxtLocalMaisIntervencionado atualizado");
            }
            else
                System.Diagnostics.Debug.WriteLine("ERRO: TxtLocalMaisIntervencionado é null");

            if (TxtTotalQuantidade != null)
            {
                TxtTotalQuantidade.Text = totalQuantidade.ToString();
                System.Diagnostics.Debug.WriteLine("TxtTotalQuantidade atualizado");
            }
            else
                System.Diagnostics.Debug.WriteLine("ERRO: TxtTotalQuantidade é null");

            if (TxtCategoriaMaisUtilizada != null)
            {
                TxtCategoriaMaisUtilizada.Text = categoriaMaisUtilizada;
                if (TxtCategoriaMaisUtilizadaValor != null) TxtCategoriaMaisUtilizadaValor.Text = categoriaMaisUtilizadaTotal.ToString();
                System.Diagnostics.Debug.WriteLine("TxtCategoriaMaisUtilizada atualizado");
            }
            else
                System.Diagnostics.Debug.WriteLine("ERRO: TxtCategoriaMaisUtilizada é null");

            if (TxtAtividadesPendentes != null)
            {
                TxtAtividadesPendentes.Text = atividadesPendentes.ToString();
                System.Diagnostics.Debug.WriteLine("TxtAtividadesPendentes atualizado");
            }
            else
                System.Diagnostics.Debug.WriteLine("ERRO: TxtAtividadesPendentes é null");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EXCEÇÃO em AtualizarEstatisticas: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
            MessageBox.Show($"Erro ao atualizar estatísticas:\n{ex.Message}\n\n{ex.StackTrace}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new AtividadeDisiaEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not AtividadeDisia atividade) return;

        var janela = new AtividadeDisiaEditWindow(atividade) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int?)CmbAno.SelectedItem ?? DateTime.Today.Year;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de atividades DISIA",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Atividades_DISIA_{ano}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaAtividadesDisia(dialog.FileName, ano);

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
