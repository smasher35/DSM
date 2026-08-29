using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class EquipamentosWindow : Window
{
    private List<Equipamento> _todos = new();

    /// <summary>Lista atualmente visível na DataGrid (já com o filtro/pesquisa aplicado). Guardada
    /// à parte para que o painel de resumo (cartões + gauges de obsolescência) seja calculado
    /// sobre exatamente os mesmos dados da grelha, sem repetir consultas à base de dados.</summary>
    private List<Equipamento> _visiveis = new();

    public EquipamentosWindow()
    {
        InitializeComponent();
        // Perfil Guest (Services/SessaoAtual.PodeEditar): acesso só de leitura a este módulo -
        // ver Services/PermissoesService.cs.
        LeiriaDISIA.Services.PermissoesService.AplicarSomenteLeituraSeGuest(BtnInserir);

        // O cabeçalho (cartões-resumo + gauges + legendas) tem um tamanho natural fixo (não muda
        // com o tamanho do ecrã), pensado para um monitor normal — em ecrãs mais baixos (ex.:
        // portáteis de 13"), reservar-lhe sempre metade da altura disponível (com um mínimo de
        // 280px, para não ficar ridiculamente pequeno nem em ecrãs minúsculos) garante que sobra
        // sempre espaço a sério para a lista de equipamento por baixo, com o cabeçalho a ganhar
        // scroll próprio se não couber tudo. Em monitores normais/grandes, o cabeçalho cabe
        // confortavelmente dentro desse teto e continua a aparecer por completo, sem scroll.
        ScrollCabecalho.MaxHeight = Math.Max(280, SystemParameters.WorkArea.Height * 0.5);

        // Modo Compacto (Administração → Aparência): troca os 3 gauges grandes por 3 barras finas
        // no painel "Distribuição por Obsolescência" — os gauges (140x140 cada) são o maior
        // consumidor de altura do cabeçalho a seguir aos cartões-resumo; em ecrãs pequenos, isto
        // devolve espaço a sério à lista de equipamento por baixo. Ambos os painéis são
        // atualizados em AtualizarResumo (ver mais abaixo) — só a visibilidade é decidida aqui,
        // uma única vez, na abertura da janela.
        if (Services.JanelaCompactaService.Ativo)
        {
            PainelObsolescenciaNormal.Visibility = Visibility.Collapsed;
            PainelObsolescenciaCompacto.Visibility = Visibility.Visible;
        }

        Recarregar();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void Recarregar()
    {
        _todos = App.Db.Equipamentos.Include(e => e.Escola).OrderByDescending(e => e.Id).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        var termo = TxtPesquisa?.Text?.Trim();
        _visiveis = string.IsNullOrWhiteSpace(termo)
            ? _todos
            : _todos.Where(e =>
                e.NumeroSerie.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                e.NumeroInventario.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                (e.Tipo?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Marca?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Escola?.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();

        Grid.ItemsSource = _visiveis;
        AtualizarResumo(_visiveis);
    }

    /// <summary>Atualiza o painel de resumo acima da DataGrid (cartões com totais dos principais
    /// tipos de equipamento + gauges com a distribuição percentual de obsolescência), a partir da
    /// MESMA lista já filtrada que alimenta a DataGrid — para os valores respeitarem sempre a
    /// pesquisa/filtro ativo, sem qualquer consulta adicional à base de dados.
    ///
    /// Os quatro tipos de equipamento e os três níveis de obsolescência usam exatamente os
    /// valores/nomes já existentes na aplicação: os tipos são os seedados em
    /// Data/DbInitializer.cs (grupo <see cref="GruposValorFixo.TipoEquipamento"/>, geridos em
    /// Administração → Dados Fixos) e os níveis vêm de <see cref="Equipamento.Obsolescencia"/>
    /// (ver <see cref="ObsolescenciaService"/>), a mesma classificação já usada no badge de
    /// "Obsolescência" da própria DataGrid.
    ///
    /// Os gauges reutilizam tal e qual o gauge circular já usado no Dashboard (mesmo controlo,
    /// mesmas cores/estilo) — ver <see cref="DashboardView.ConstruirGaugePercentagem"/>.</summary>
    private void AtualizarResumo(List<Equipamento> visiveis)
    {
        // ---- Linha 1: total geral + totais dos principais tipos de equipamento ----
        TxtTotalGeral.Text = visiveis.Count.ToString();
        TxtTotalSecretaria.Text = visiveis.Count(e => TipoEquivale(e.Tipo, "Computador de Secretária")).ToString();
        TxtTotalPortateis.Text = visiveis.Count(e => TipoEquivale(e.Tipo, "Portátil")).ToString();
        TxtTotalAccessPoints.Text = visiveis.Count(e => TipoEquivale(e.Tipo, "Access Point")).ToString();
        TxtTotalSwitches.Text = visiveis.Count(e => TipoEquivale(e.Tipo, "Switch")).ToString();

        AtualizarMaisIntervencionado(visiveis);

        // ---- Linha 2: gauges de obsolescência (% sobre o total de equipamento visível) ----
        var total = visiveis.Count;
        var totalAtual = visiveis.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.Atual);
        var totalMonitorizar = visiveis.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.AMonitorizar);
        var totalObsoleto = visiveis.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.Obsoleto);

        TxtTotalObsolescencia.Text = total == 0
            ? "% de equipamento em cada nível de obsolescência (sem equipamento a apresentar)"
            : $"% de equipamento em cada nível de obsolescência ({total} equipamentos)";

        // DashboardView.ConstruirGaugePercentagem já trata total == 0 devolvendo 0%, em vez de
        // dividir por zero (ver Views/DashboardView.xaml.cs).
        GaugeObsolescenciaAtual.Series = DashboardView.ConstruirGaugePercentagem(totalAtual, total, "#22C55E");
        TxtGaugeObsolescenciaAtual.Text = $"{totalAtual} / {total}";

        GaugeObsolescenciaMonitorizar.Series = DashboardView.ConstruirGaugePercentagem(totalMonitorizar, total, "#F59E0B");
        TxtGaugeObsolescenciaMonitorizar.Text = $"{totalMonitorizar} / {total}";

        GaugeObsolescenciaObsoleto.Series = DashboardView.ConstruirGaugePercentagem(totalObsoleto, total, "#EF4444");
        TxtGaugeObsolescenciaObsoleto.Text = $"{totalObsoleto} / {total}";

        // Versão compacta (Modo Compacto) do mesmo resumo — 3 barras finas em vez dos 3 gauges
        // acima. As colunas "Cheia"/"Vazia" de cada barra usam larguras em "*" (estrela) — que se
        // ajustam automaticamente à largura real do ecrã — em vez de pixels fixos, para a barra
        // preencher corretamente a percentagem certa seja qual for o tamanho da janela.
        AtualizarBarraCompacta(ColBarraAtualCheia, ColBarraAtualVazia, TxtBarraAtual, totalAtual, total);
        AtualizarBarraCompacta(ColBarraMonitorizarCheia, ColBarraMonitorizarVazia, TxtBarraMonitorizar, totalMonitorizar, total);
        AtualizarBarraCompacta(ColBarraObsoletoCheia, ColBarraObsoletoVazia, TxtBarraObsoleto, totalObsoleto, total);
    }

    /// <summary>Ajusta a largura preenchida de uma barra fina do painel compacto de obsolescência
    /// (ver <see cref="AtualizarResumo"/>) para refletir a percentagem de <paramref name="parcela"/>
    /// sobre <paramref name="total"/>, e atualiza o texto "x / total" ao lado.</summary>
    private static void AtualizarBarraCompacta(ColumnDefinition colCheia, ColumnDefinition colVazia, TextBlock txt, int parcela, int total)
    {
        var percentagem = total == 0 ? 0 : (parcela * 100.0 / total);
        colCheia.Width = new GridLength(percentagem, GridUnitType.Star);
        colVazia.Width = new GridLength(100 - percentagem, GridUnitType.Star);
        txt.Text = $"{parcela} / {total}";
    }

    /// <summary>Compara o "Tipo" de um equipamento com um dos valores fixos seedados para o grupo
    /// <see cref="GruposValorFixo.TipoEquipamento"/> (ver Data/DbInitializer.cs), ignorando
    /// diferenças de maiúsculas/minúsculas — para não deixar de contar um equipamento só porque o
    /// texto foi escrito com uma capitalização ligeiramente diferente.</summary>
    private static bool TipoEquivale(string? tipo, string alvo) =>
        !string.IsNullOrWhiteSpace(tipo) && tipo.Equals(alvo, StringComparison.OrdinalIgnoreCase);

    /// <summary>Encontra, dentro da lista visível, o equipamento com mais intervenções somando as
    /// mesmas duas fontes já usadas no contador individual "Nº de Vezes Intervencionado" de cada
    /// equipamento (ver EquipamentoEditWindow.xaml.cs → CarregarHistoricoIntervencoes): intervenções
    /// no local (IntervencaoEquipamentos) e recolhas para a DISIA (EquipamentosRecolhidos). Os dois
    /// totais são pré-calculados de uma só vez por tabela (GroupBy), em vez de uma consulta por
    /// equipamento, para não repetir consultas à base de dados desnecessariamente com listas
    /// grandes.</summary>
    private void AtualizarMaisIntervencionado(List<Equipamento> visiveis)
    {
        if (visiveis.Count == 0) { PainelMaisIntervencionado.Visibility = Visibility.Collapsed; return; }

        var idsVisiveis = visiveis.Select(e => e.Id).ToHashSet();

        var porLocal = App.Db.IntervencaoEquipamentos
            .Where(ie => idsVisiveis.Contains(ie.EquipamentoId))
            .GroupBy(ie => ie.EquipamentoId)
            .Select(g => new { EquipamentoId = g.Key, Total = g.Count() })
            .ToDictionary(x => x.EquipamentoId, x => x.Total);

        var porRecolha = App.Db.EquipamentosRecolhidos
            .Where(r => idsVisiveis.Contains(r.EquipamentoId))
            .GroupBy(r => r.EquipamentoId)
            .Select(g => new { EquipamentoId = g.Key, Total = g.Count() })
            .ToDictionary(x => x.EquipamentoId, x => x.Total);

        var maisIntervencionado = visiveis
            .Select(e => new
            {
                Equipamento = e,
                Total = porLocal.GetValueOrDefault(e.Id) + porRecolha.GetValueOrDefault(e.Id)
            })
            .Where(x => x.Total > 0)
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();

        if (maisIntervencionado == null)
        {
            PainelMaisIntervencionado.Visibility = Visibility.Collapsed;
            return;
        }

        PainelMaisIntervencionado.Visibility = Visibility.Visible;
        var eq = maisIntervencionado.Equipamento;
        var vezes = maisIntervencionado.Total == 1 ? "1 intervenção" : $"{maisIntervencionado.Total} intervenções";
        TxtMaisIntervencionado.Text = $" {eq.NumeroSerie} — {eq.Tipo} ({eq.Escola?.Nome ?? "sem escola associada"}) — {vezes}";
    }

    private void Filtro_Changed(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void Inserir_Click(object sender, RoutedEventArgs e)
    {
        var janela = new EquipamentoEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not Equipamento equipamento) return;

        var janela = new EquipamentoEditWindow(equipamento) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) Recarregar();
    }

    private void Relatorio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório de equipamento informático",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Equipamento_Informatico_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new LeiriaDISIA.Services.RelatorioService(App.Db);
            servico.GerarListaEquipamento(dialog.FileName);

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

    // Item 3.3: aberta a partir daqui (e não de um hipotético "Relatórios → Equipamentos" central,
    // que não existe nesta aplicação — cada módulo tem o seu próprio botão de relatório) porque é
    // exatamente aqui que o utilizador já está no contexto de um equipamento/escola específicos
    // (pode inclusive já ter pesquisado por uma escola em TxtPesquisa antes de clicar), o que
    // corresponde à preferência indicada: "preferencialmente no módulo Equipamentos Informáticos,
    // se o utilizador puder selecionar uma escola/contexto antes de gerar".
    private void FolhaInventario_Click(object sender, RoutedEventArgs e)
    {
        var janela = new FolhaInventarioWindow { Owner = this };
        janela.ShowDialog();
    }
}
