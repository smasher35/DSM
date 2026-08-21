using LeiriaDISIA.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LeiriaDISIA.Services;

public partial class RelatorioService
{
    private static void ComposeAtividadesDisia(ColumnDescriptor col, IReadOnlyList<AtividadeDisia> atividades, string mesFormatado)
    {
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;
        const string corBandaCategoria = "#2D4A6A";
        const string corSubtotal = "#EDEDED";

        col.Item().Section(SecaoAtividadesDisia).Text("Atividades na DISIA").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
        col.Item().PaddingTop(8);
        foreach (var paragrafo in DividirEmParagrafos(
            $"Durante o mês de {mesFormatado} foram realizadas as seguintes atividades pela DISIA, fora do " +
            "âmbito direto de uma intervenção numa escola — em instalações municipais, juntas de freguesia e " +
            "outros equipamentos do concelho de Leiria, agrupadas por categoria."))
        {
            col.Item().PaddingBottom(6).Text(paragrafo).FontSize(10).FontColor(Colors.Grey.Darken3).LineHeight(1.3f).Justify();
        }
        col.Item().PaddingBottom(8);

        if (atividades.Count == 0)
        {
            SemRegistos(col, "Não foram registadas atividades da DISIA neste mês.");
            return;
        }

        static string CorEstadoAtividade(EstadoIntervencao estado) => estado switch
        {
            EstadoIntervencao.Fechada => Colors.Green.Darken2,
            EstadoIntervencao.Pendente => Colors.Red.Darken1,
            EstadoIntervencao.EmProgresso => Colors.Orange.Darken2,
            _ => Colors.Grey.Darken1,
        };

        var porCategoria = atividades
            .GroupBy(a => a.Categoria?.Nome ?? "(Sem Categoria)")
            .OrderBy(g => g.Key)
            .ToList();

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(52);
                cols.RelativeColumn(2.2f);
                cols.RelativeColumn(3.4f);
                cols.ConstantColumn(55);
                cols.ConstantColumn(65);
            });

            table.Header(h =>
            {
                h.Cell().Element(CellHeaderPadrao).Text("Data").FontSize(7).Bold().FontColor(branco);
                h.Cell().Element(CellHeaderPadrao).Text("Local").FontSize(7).Bold().FontColor(branco);
                h.Cell().Element(CellHeaderPadrao).Text("Descrição").FontSize(7).Bold().FontColor(branco);
                h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Qtd.").FontSize(7).Bold().FontColor(branco);
                h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Estado").FontSize(7).Bold().FontColor(branco);
            });

            var indiceLinha = 0;
            foreach (var grupo in porCategoria)
            {
                table.Cell().ColumnSpan(5).Element(c => c.Background(corBandaCategoria).Padding(5))
                    .Text(grupo.Key.ToUpperInvariant()).FontSize(9).Bold().FontColor(branco);

                foreach (var a in grupo.OrderBy(x => x.Data))
                {
                    var bg = indiceLinha % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(a.Data.ToString("dd-MM-yyyy")).FontSize(7.5f);
                    table.Cell().Element(Cell).Text(a.Local ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(a.Descricao).FontSize(7.5f);
                    table.Cell().Element(Cell).AlignCenter().Text(a.Quantidade.ToString()).FontSize(7.5f).Bold();
                    table.Cell().Element(Cell).AlignCenter().Text(a.Estado.ToString()).FontSize(7)
                        .FontColor(CorEstadoAtividade(a.Estado));
                    indiceLinha++;
                }

                var totalAtividades = grupo.Count();
                var totalQuantidade = grupo.Sum(x => x.Quantidade);
                table.Cell().ColumnSpan(5).Element(c => c.Background(corSubtotal).Padding(4))
                    .Text($"Subtotal {grupo.Key}: {totalAtividades} atividade(s)  ·  {totalQuantidade} serviço(s) prestado(s)")
                    .FontSize(7).Bold().FontColor(Colors.Grey.Darken2);
            }

            table.Cell().ColumnSpan(5).Element(c => c.Background("#1F4E79").Padding(6))
                .Text($"TOTAL GERAL: {atividades.Count} atividade(s)  ·  {atividades.Sum(a => a.Quantidade)} serviço(s) prestado(s)")
                .FontSize(9).Bold().FontColor(branco);
        });
    }
}
