using LeiriaDISIA.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LeiriaDISIA.Services;

/// <summary>
/// Gera um relatório em PDF de uma única intervenção (para impressão/arquivo), usando QuestPDF
/// (licença Community, gratuita). Segue a mesma linguagem visual (cores, tipografia, cabeçalho
/// e rodapé) do Relatório Mensal de Atividades, para que todos os PDFs da aplicação pareçam
/// pertencer ao mesmo "produto".
/// </summary>
public class IntervencaoPdfService
{
    private const string CorNavy = "#1F4E79";
    private const string CorNavyEscuro = "#16334D";
    private const string CorTeal = "#2AB7CA";
    private const string CorFundoCaixa = "#F4F6F9";
    private const string CorFundoAlternado = "#F8FAFC";
    private const string CorBorda = "#E2E8F0";

    static IntervencaoPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>Uma linha genérica de uma das tabelas de equipamento (intervencionado, recolhido
    /// ou abatido), já formatada em texto, para poderem ser desenhadas pelo mesmo helper de tabela.</summary>
    private readonly record struct LinhaEquipamentoPdf(
        string Descricao, string NumeroSerie, string NumeroInventario, string InfoExtra, string Observacoes);

    public string Gerar(
        Intervencao intervencao,
        IReadOnlyList<EquipamentoRecolhido> recolhidos,
        IReadOnlyList<EquipamentoAbatido> abatidos,
        string caminhoDestino)
    {
        var linhasIntervencionados = intervencao.EquipamentosIntervencionados
            .Where(ie => ie.Equipamento != null)
            .Select(ie => new LinhaEquipamentoPdf(
                DescricaoEquipamento(ie.Equipamento!.Tipo, ie.Equipamento.Marca, ie.Equipamento.Modelo),
                ie.Equipamento.NumeroSerie,
                ie.Equipamento.NumeroInventario,
                "",
                ie.Observacoes ?? ""))
            .ToList();

        var linhasRecolhidos = recolhidos
            .Select(r => new LinhaEquipamentoPdf(
                r.Equipamento == null ? "-" : DescricaoEquipamento(r.Equipamento.Tipo, r.Equipamento.Marca, r.Equipamento.Modelo),
                r.Equipamento?.NumeroSerie ?? "-",
                r.Equipamento?.NumeroInventario ?? "-",
                $"{r.Estado}\n{r.DataRecolha:dd-MM-yyyy}",
                r.Observacoes ?? ""))
            .ToList();

        var linhasAbatidos = abatidos
            .Select(a => new LinhaEquipamentoPdf(
                a.Equipamento != null
                    ? DescricaoEquipamento(a.Equipamento.Tipo, a.Equipamento.Marca, a.Equipamento.Modelo)
                    : (a.DescricaoEquipamento ?? "-"),
                a.Equipamento?.NumeroSerie ?? (a.NumeroSerie ?? "-"),
                a.Equipamento?.NumeroInventario ?? (a.NumeroInventario ?? ""),
                $"{a.Status}\n{a.DataAbate:dd-MM-yyyy}",
                a.Observacoes ?? ""))
            .ToList();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeCabecalho(c, intervencao));
                page.Content().PaddingTop(16).Column(col =>
                {
                    ComposeInfoCard(col, intervencao);

                    col.Item().PaddingTop(18);
                    ComposeCaixaTexto(col, "Descrição / Tipo de Intervenção", intervencao.Descricao);

                    if (linhasIntervencionados.Count > 0)
                    {
                        col.Item().PaddingTop(18);
                        ComposeTabelaEquipamento(col, "Equipamento Intervencionado no Local", CorNavy,
                            null, linhasIntervencionados);
                    }

                    if (linhasRecolhidos.Count > 0)
                    {
                        col.Item().PaddingTop(18);
                        ComposeTabelaEquipamento(col, "Equipamento Recolhido para a DISIA", "#7C3AED",
                            "Estado / Data", linhasRecolhidos);
                    }

                    if (linhasAbatidos.Count > 0)
                    {
                        col.Item().PaddingTop(18);
                        ComposeTabelaEquipamento(col, "Equipamento Abatido", "#EF4444",
                            "Estado / Data", linhasAbatidos);
                    }

                    if (!string.IsNullOrWhiteSpace(intervencao.MaterialRecolhidoAbatido))
                    {
                        col.Item().PaddingTop(18);
                        ComposeCaixaTexto(col, "Notas Adicionais (registo histórico)", intervencao.MaterialRecolhidoAbatido);
                    }
                });
                page.Footer().Element(ComposeRodape);
            });
        }).GeneratePdf(caminhoDestino);

        return caminhoDestino;
    }

    private static string DescricaoEquipamento(string? tipo, string? marca, string? modelo) =>
        string.Join(" ", new[] { tipo, marca, modelo }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string FormatarEstado(EstadoIntervencao estado) => estado switch
    {
        EstadoIntervencao.EmProgresso => "Em Progresso",
        EstadoIntervencao.EmEspera => "Em Espera",
        _ => estado.ToString()
    };

    private static string FormatarCategoria(IntervencaoCategoria ic)
    {
        var texto = ic.Categoria?.Nome ?? "Categoria";
        if (ic.SubCategoria != null) texto += $" · {ic.SubCategoria.Nome}";
        if (ic.Quantidade > 1) texto += $" (x{ic.Quantidade})";
        return texto;
    }

    /// <summary>Cabeçalho: identidade visual (logo + título + subtítulo), uma faixa de destaque
    /// de duas cores (igual à usada no Relatório Mensal) e, do lado direito, o nº e a data da
    /// intervenção — para o documento ser identificável de imediato, mesmo impresso avulso.</summary>
    private static void ComposeCabecalho(IContainer container, Intervencao intervencao)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.ConstantItem(40).Height(40).Image(AppAssets.LogoDisia).FitArea();
                row.RelativeItem().PaddingLeft(12).Column(c =>
                {
                    c.Item().Text("MUNICÍPIO DE LEIRIA — DISIA").FontSize(8).Bold()
                        .FontColor(Colors.Grey.Darken1).LetterSpacing(0.06f);
                    c.Item().Text("Relatório de Intervenção").FontSize(19).Bold().FontColor(CorNavy);
                });
                row.ConstantItem(150).AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text($"Intervenção Nº {intervencao.Id}").FontSize(10).Bold().FontColor(CorNavyEscuro);
                    c.Item().AlignRight().Text(intervencao.Data.ToString("dd 'de' MMMM 'de' yyyy"))
                        .FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                });
            });
            col.Item().PaddingTop(8).Height(3).Background(CorNavy);
            col.Item().Height(1.4f).Background(CorTeal);
            col.Item().PaddingBottom(4);
        });
    }

    private static void ComposeRodape(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Height(1).Background(Colors.Grey.Lighten2);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text("Câmara Municipal de Leiria — DISIA").FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);
                row.ConstantItem(160).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Colors.Grey.Darken1));
                    t.Span("Gerado em ");
                    t.Span(DateTime.Now.ToString("dd-MM-yyyy HH:mm"));
                });
            });
            col.Item().PaddingTop(1).AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Colors.Grey.Darken1));
                t.Span("Página ");
                t.CurrentPageNumber();
                t.Span(" de ");
                t.TotalPages();
            });
        });
    }

    /// <summary>Cartão de resumo com os dados essenciais da intervenção (escola, agrupamento,
    /// data, estado e categorias), num fundo cinza claro com cantos arredondados — dá um "resumo
    /// visual" imediato antes de entrar no detalhe da descrição e das tabelas de equipamento.</summary>
    private static void ComposeInfoCard(ColumnDescriptor col, Intervencao intervencao)
    {
        col.Item().Background(CorFundoCaixa).Padding(14).Column(c =>
        {
            c.Item().Row(row =>
            {
                row.RelativeItem(2).Column(cc =>
                {
                    cc.Item().Text("ESCOLA").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                    cc.Item().Text(intervencao.Escola?.Nome ?? "-").FontSize(13).Bold().FontColor(CorNavyEscuro);
                });
                row.RelativeItem().Column(cc =>
                {
                    cc.Item().Text("AGRUPAMENTO").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                    cc.Item().Text(intervencao.Agrupamento?.Nome ?? "-").FontSize(9.5f).FontColor(Colors.Grey.Darken3);
                });
                row.ConstantItem(110).Column(cc =>
                {
                    cc.Item().AlignRight().Text("ESTADO").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                    cc.Item().AlignRight().Element(e => Selo(e, FormatarEstado(intervencao.Estado), intervencao.CorEstado));
                });
            });

            c.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem(2).Column(cc =>
                {
                    cc.Item().Text("LOCALIDADE").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                    cc.Item().Text(intervencao.Escola?.Localidade ?? "-").FontSize(9.5f).FontColor(Colors.Grey.Darken3);
                });
                row.RelativeItem(3).Column(cc =>
                {
                    cc.Item().Text("CATEGORIAS").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                    if (intervencao.Categorias.Count == 0)
                    {
                        cc.Item().PaddingTop(2).Text("-").FontSize(9.5f).FontColor(Colors.Grey.Darken3);
                    }
                    else
                    {
                        cc.Item().PaddingTop(3).Row(chipsRow =>
                        {
                            chipsRow.Spacing(5);
                            foreach (var ic in intervencao.Categorias)
                                chipsRow.AutoItem().Element(e => Selo(e, FormatarCategoria(ic), ic.Categoria?.CorHex ?? "#64748B"));
                        });
                    }
                });
            });

            if (intervencao.Estado is EstadoIntervencao.Pendente or EstadoIntervencao.EmEspera &&
                !string.IsNullOrWhiteSpace(intervencao.MotivoPendente))
            {
                c.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Column(cc =>
                    {
                        cc.Item().Text("MOTIVO").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                        cc.Item().Text(intervencao.MotivoPendente!).FontSize(9.5f).FontColor(Colors.Grey.Darken3);
                    });
                });
            }
        });
    }

    /// <summary>Desenha um "selo"/badge (fundo colorido, texto branco, cantos arredondados) —
    /// usado para o estado da intervenção e para cada categoria/subcategoria.</summary>
    private static void Selo(IContainer container, string texto, string corHex)
    {
        container.Background(corHex).PaddingVertical(3).PaddingHorizontal(9)
            .Text(texto).FontSize(8).Bold().FontColor(Colors.White);
    }

    /// <summary>Bloco de texto livre (descrição, notas) apresentado como uma secção com um título
    /// de destaque (barra de cor à esquerda) e o conteúdo numa caixa cinza clara.</summary>
    private static void ComposeCaixaTexto(ColumnDescriptor col, string titulo, string? conteudo)
    {
        TituloSeccao(col, titulo, CorNavy);
        col.Item().PaddingTop(6).Background(CorFundoCaixa).Padding(12)
            .Text(string.IsNullOrWhiteSpace(conteudo) ? "-" : conteudo).FontSize(10).LineHeight(1.3f);
    }

    /// <summary>Título de secção com uma barra vertical colorida à esquerda (identidade visual
    /// consistente em todas as tabelas/blocos do relatório).</summary>
    private static void TituloSeccao(ColumnDescriptor col, string titulo, string corHex)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(4).Height(15).Background(corHex);
            row.RelativeItem().PaddingLeft(8).AlignMiddle().Text(titulo).FontSize(12.5f).Bold().FontColor(CorNavyEscuro);
        });
    }

    /// <summary>Desenha a tabela de uma das três listas de equipamento (intervencionado, recolhido
    /// ou abatido). Quando <paramref name="rotuloColunaExtra"/> é nulo, a coluna de estado/data não
    /// é desenhada (caso do equipamento intervencionado no local, que não tem esse conceito).</summary>
    private static void ComposeTabelaEquipamento(
        ColumnDescriptor col, string titulo, string corAccent,
        string? rotuloColunaExtra, IReadOnlyList<LinhaEquipamentoPdf> linhas)
    {
        TituloSeccao(col, titulo, corAccent);

        col.Item().PaddingTop(6).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(2.6f);
                cols.ConstantColumn(75);
                cols.ConstantColumn(75);
                if (rotuloColunaExtra != null) cols.ConstantColumn(85);
                cols.RelativeColumn(2f);
            });

            IContainer CabecalhoCelula(IContainer c) => c.Background(corAccent).PaddingVertical(6).PaddingHorizontal(6).AlignMiddle();

            table.Header(h =>
            {
                h.Cell().Element(CabecalhoCelula).Text("Equipamento").FontSize(8).Bold().FontColor(Colors.White);
                h.Cell().Element(CabecalhoCelula).Text("Nº Série").FontSize(8).Bold().FontColor(Colors.White);
                h.Cell().Element(CabecalhoCelula).Text("Nº Inventário").FontSize(8).Bold().FontColor(Colors.White);
                if (rotuloColunaExtra != null)
                    h.Cell().Element(CabecalhoCelula).Text(rotuloColunaExtra).FontSize(8).Bold().FontColor(Colors.White);
                h.Cell().Element(CabecalhoCelula).Text("Observações").FontSize(8).Bold().FontColor(Colors.White);
            });

            for (var i = 0; i < linhas.Count; i++)
            {
                var linha = linhas[i];
                var bg = i % 2 == 0 ? "#FFFFFF" : CorFundoAlternado;
                IContainer Cell(IContainer c) => c.Background(bg).BorderBottom(1).BorderColor(CorBorda)
                    .PaddingVertical(6).PaddingHorizontal(6);

                table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(linha.Descricao) ? "-" : linha.Descricao).FontSize(8.5f).Bold();
                table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(linha.NumeroSerie) ? "-" : linha.NumeroSerie).FontSize(8.5f);
                table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(linha.NumeroInventario) ? "-" : linha.NumeroInventario).FontSize(8.5f);
                if (rotuloColunaExtra != null)
                    table.Cell().Element(Cell).Text(linha.InfoExtra).FontSize(8).FontColor(Colors.Grey.Darken2);
                table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(linha.Observacoes) ? "-" : linha.Observacoes).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
            }
        });

        col.Item().PaddingTop(3).Text($"Total: {linhas.Count} equipamento(s)").FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
    }
}
