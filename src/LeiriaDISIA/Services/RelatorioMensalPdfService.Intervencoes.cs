using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LeiriaDISIA.Services;

public partial class RelatorioService
{
    private static void ComposeIntervencoesEscolares(ColumnDescriptor col, int totalEdificios, int totalAgrupamentos,
        IReadOnlyList<(string Agrupamento, int Total, int Fechadas, int Pendentes, int EmProgresso, int EmEspera)> porAgrupamento,
        IReadOnlyList<(string Categoria, int Total)> porCategoria,
        IReadOnlyList<string> agrupamentosComIntervencoes,
        IReadOnlyList<(string Categoria, IReadOnlyList<int> Valores)> cruzamentoTipoAgrupamento,
        IReadOnlyDictionary<string, string> coresPorCategoria,
        string periodo)
    {
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        col.Item().Section(SecaoIntervencoes).Text("Intervenções Escolares").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
        col.Item().PaddingTop(8);
        foreach (var paragrafo in DividirEmParagrafos(
            $"No âmbito das minhas funções profissionais, asseguro o suporte informático a todas as escolas e " +
            $"jardins de infância do concelho de Leiria, abrangendo um total de {totalEdificios} edifícios " +
            $"escolares distribuídos por {totalAgrupamentos} Agrupamentos de Escolas. " +
            "As intervenções realizadas incidem maioritariamente na resolução de anomalias e na manutenção dos " +
            "sistemas informáticos, incluindo a formatação e reinstalação de equipamentos, reposição de software, " +
            "substituição e reparação de componentes de hardware, configuração de equipamentos de rede e de " +
            "ligações VPN para acesso das assistentes operacionais à rede do Município. Adicionalmente, sou " +
            "responsável pela instalação, configuração e substituição de equipamentos audiovisuais, garantindo o " +
            "seu correto funcionamento e suporte técnico."))
        {
            col.Item().PaddingBottom(6).Text(paragrafo).FontSize(10).FontColor(Colors.Grey.Darken3).LineHeight(1.3f).Justify();
        }
        col.Item().PaddingBottom(8);

        // ---- 3.2 — Intervenções por Agrupamento (mês corrente): mesmo gráfico de barras + tabela
        // de detalhe por estado já usados no "Resumo de Intervenções por Agrupamento" (ver
        // GerarResumoIntervencoesPorAgrupamento), em vez da antiga tabela com todos os agrupamentos
        // (mesmo a 0) + gráfico de pizza — só entram aqui os agrupamentos com intervenções no mês,
        // ordenados do mais para o menos intervencionado, tal como no resumo.
        col.Item().Section(SecaoIntervencoesAgrupamento).PaddingBottom(6)
            .Text("Intervenções por Agrupamento").FontSize(13).Bold().FontColor(Colors.Blue.Darken2);

        var porAgrupamentoComDados = porAgrupamento.Where(a => a.Total > 0).OrderByDescending(a => a.Total).ToList();

        if (porAgrupamentoComDados.Count > 0)
        {
            // Mesmos 3 cartões de resumo (Intervenções Válidas / Agrupamentos Envolvidos / Mais
            // Intervencionado) já usados no "Resumo de Intervenções por Agrupamento" (ver
            // GerarResumoIntervencoesPorAgrupamento), aqui a uma escala ligeiramente menor para
            // ficarem bem enquadrados por cima do gráfico de barras, dentro desta secção.
            var totalIntervencoesMes = porAgrupamentoComDados.Sum(a => a.Total);
            var maisIntervencionado = porAgrupamentoComDados[0];
            var cardsAgrupamento = new (string, string, string)[]
            {
                (totalIntervencoesMes.ToString(), $"Intervenções Válidas — {periodo}", Colors.Blue.Darken2),
                (porAgrupamentoComDados.Count.ToString(), "Agrupamentos Envolvidos", Colors.Purple.Darken1),
                (maisIntervencionado.Total.ToString(), $"Mais Intervencionado: {maisIntervencionado.Agrupamento}", Colors.Orange.Darken2),
            };
            DesenharCartoesResumo(col, cardsAgrupamento, escala: 0.85f);

            GraficoBarras(col, "Intervenções por Agrupamento", porAgrupamentoComDados.Select(a => (a.Agrupamento, a.Total)).ToList());
            col.Item().PaddingTop(2).PaddingBottom(6).AlignCenter()
                .Text("Figura 1 — Intervenções por agrupamento, no mês.")
                .FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);

            col.Item().PaddingBottom(4).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3.2f);
                    cols.ConstantColumn(45);
                    cols.ConstantColumn(55);
                    cols.ConstantColumn(60);
                    cols.ConstantColumn(70);
                    cols.ConstantColumn(60);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Agrupamento").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Total").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Fechadas").FontSize(6.5f).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Pendentes").FontSize(6.5f).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Em Progresso").FontSize(6).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Em Espera").FontSize(6.5f).Bold().FontColor(branco);
                });

                for (var i = 0; i < porAgrupamentoComDados.Count; i++)
                {
                    var a = porAgrupamentoComDados[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(a.Agrupamento).FontSize(8).Bold();
                    table.Cell().Element(Cell).AlignCenter().Text(a.Total.ToString()).FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                    table.Cell().Element(Cell).AlignCenter().Text(a.Fechadas.ToString()).FontSize(7.5f).FontColor(Colors.Green.Darken2);
                    table.Cell().Element(Cell).AlignCenter().Text(a.Pendentes.ToString()).FontSize(7.5f).FontColor(Colors.Red.Darken1);
                    table.Cell().Element(Cell).AlignCenter().Text(a.EmProgresso.ToString()).FontSize(7.5f).FontColor(Colors.Orange.Darken2);
                    table.Cell().Element(Cell).AlignCenter().Text(a.EmEspera.ToString()).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                }
            });
            col.Item().PaddingBottom(10);
        }
        else
        {
            SemRegistos(col, "Não foram registadas intervenções por agrupamento neste mês.");
        }

        col.Item().PageBreak();

        // ---- 3.4 e 3.5 — Totais por Tipo de Intervenção ----
        col.Item().Section(SecaoIntervencoesTipo).PaddingBottom(6)
            .Text("Totais por Tipo de Intervenção").FontSize(13).Bold().FontColor(Colors.Blue.Darken2);

        if (cruzamentoTipoAgrupamento.Count > 0 && agrupamentosComIntervencoes.Count > 0)
        {
            GraficoBarrasAgrupadas(col, "Tipos de Intervenção por Agrupamento", agrupamentosComIntervencoes, cruzamentoTipoAgrupamento, coresPorCategoria);
            col.Item().PaddingTop(2).PaddingBottom(4).AlignCenter()
                .Text("Figura 2 — Topologia das intervenções nas escolas, por tipo e por agrupamento.")
                .FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);

            foreach (var paragrafo in DividirEmParagrafos(
                "Nota: Uma ida a cada escola é considerada apenas como uma intervenção, mas pode englobar várias " +
                "áreas de intervenção numa só visita, daí o total por tipo e categoria de intervenção poder ser " +
                "superior ao total de intervenções."))
            {
                col.Item().PaddingBottom(6).Text(paragrafo).FontSize(8).Italic().FontColor(Colors.Grey.Darken2).Justify();
            }
            col.Item().PaddingBottom(8);
        }

        if (porCategoria.Count > 0)
        {
            GraficoBarras(col, "Total de Intervenções por Categoria", porCategoria);
            col.Item().PaddingTop(2).AlignCenter()
                .Text("Figura 3 — Total de intervenções por categoria, no mês.")
                .FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);
        }

        if (cruzamentoTipoAgrupamento.Count == 0 && porCategoria.Count == 0)
            SemRegistos(col, "Não foram registadas intervenções com categorias associadas neste mês.");
    }
}
