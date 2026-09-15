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
        IReadOnlyList<EquipamentoRecolhido> devolvidos,
        IReadOnlyList<IntervencaoEquipamentoNovo> novosEntregues,
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

        // Equipamento devolvido à escola durante ESTA intervenção (distinto do que foi recolhido
        // nela — pode ter sido recolhido numa visita anterior e só devolvido agora, ver
        // EquipamentoRecolhido.IntervencaoEntregaId). "InfoExtra" mostra a data da devolução, não a
        // da recolha original (essa já não é relevante aqui — o que importa neste relatório é que
        // o equipamento voltou à escola nesta intervenção).
        var linhasDevolvidos = devolvidos
            .Select(d => new LinhaEquipamentoPdf(
                d.Equipamento == null ? "-" : DescricaoEquipamento(d.Equipamento.Tipo, d.Equipamento.Marca, d.Equipamento.Modelo),
                d.Equipamento?.NumeroSerie ?? "-",
                d.Equipamento?.NumeroInventario ?? "-",
                d.DataEntrega?.ToString("dd-MM-yyyy") ?? "-",
                d.Observacoes ?? ""))
            .ToList();

        // Equipamento novo (sem escola anterior) entregue e instalado nesta escola pela primeira
        // vez nesta intervenção — distinto do devolvido acima (esse pressupõe uma recolha
        // anterior; este nunca lá esteve). Sem "Observações" nem "InfoExtra" próprios (ver
        // Models/Intervencao.cs, IntervencaoEquipamentoNovo) — a própria secção já deixa claro do
        // que se trata, e ComposeTabelaEquipamento aceita null para não desenhar essa coluna.
        var linhasNovosEntregues = novosEntregues
            .Select(n => new LinhaEquipamentoPdf(
                n.Equipamento == null ? "-" : DescricaoEquipamento(n.Equipamento.Tipo, n.Equipamento.Marca, n.Equipamento.Modelo),
                n.Equipamento?.NumeroSerie ?? "-",
                n.Equipamento?.NumeroInventario ?? "-",
                "",
                ""))
            .ToList();

        // Junta as observações de todas as listas acima que as tenham preenchidas (a maior parte
        // não tem) — para as listar todas juntas no fim do documento, em vez de uma coluna
        // "Observações" em cada tabela (ver ComposeObservacoesFinais/ComposeTabelaEquipamento).
        // "Equipamento Novo Entregue" não tem Observações (ver Models/Intervencao.cs,
        // IntervencaoEquipamentoNovo), por isso não entra aqui.
        var observacoesParaListar = new List<(string Secao, LinhaEquipamentoPdf Linha)>();
        observacoesParaListar.AddRange(linhasIntervencionados.Select(l => ("Equipamento Intervencionado no Local", l)));
        observacoesParaListar.AddRange(linhasRecolhidos.Select(l => ("Equipamento Recolhido para a DISIA", l)));
        observacoesParaListar.AddRange(linhasDevolvidos.Select(l => ("Equipamento Devolvido à Escola", l)));
        observacoesParaListar.AddRange(linhasAbatidos.Select(l => ("Equipamento Abatido", l)));
        observacoesParaListar = observacoesParaListar.Where(x => !string.IsNullOrWhiteSpace(x.Linha.Observacoes)).ToList();

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

                    if (linhasDevolvidos.Count > 0)
                    {
                        col.Item().PaddingTop(18);
                        ComposeTabelaEquipamento(col, "Equipamento Devolvido à Escola", "#0F766E",
                            "Data da Devolução", linhasDevolvidos);
                    }

                    if (linhasNovosEntregues.Count > 0)
                    {
                        col.Item().PaddingTop(18);
                        ComposeTabelaEquipamento(col, "Equipamento Novo Entregue à Escola", "#0891B2",
                            null, linhasNovosEntregues);
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

                    ComposeObservacoesFinais(col, observacoesParaListar);
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
            });

            // "CATEGORIAS" fica agora na sua própria linha, a toda a largura — não a partilhar
            // com "Localidade" como antes — para dar o máximo de espaço possível aos selos: uma
            // intervenção pode ter todas as categorias envolvidas (ex.: apetrechamento completo de
            // uma escola nova), e Row() em QuestPDF NÃO passa sozinho os itens que não cabem para
            // uma nova linha (ao contrário de um WrapPanel em WPF) — com largura a menos e vários
            // selos de nomes compridos (ex.: "Redes e Comunicações"), isto já chegou a impedir todo
            // o PDF de ser gerado ("conflicting size constraints"). Como proteção adicional, mesmo
            // com toda a largura da página disponível, os selos continuam agrupados em blocos de
            // no máximo 3 por linha (ver maxSelosPorLinha) — sem esta segunda salvaguarda, uma
            // lista de categorias suficientemente comprida (ou nomes suficientemente longos)
            // poderia voltar a ultrapassar a largura disponível mesmo a toda a largura da página.
            c.Item().PaddingTop(10).Column(cc =>
            {
                cc.Item().Text("CATEGORIAS").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);

                if (intervencao.Categorias.Count == 0)
                {
                    cc.Item().PaddingTop(2).Text("-").FontSize(9.5f).FontColor(Colors.Grey.Darken3);
                }
                else
                {
                    const int maxSelosPorLinha = 3;
                    var categorias = intervencao.Categorias.ToList();
                    for (var i = 0; i < categorias.Count; i += maxSelosPorLinha)
                    {
                        var linha = categorias.Skip(i).Take(maxSelosPorLinha).ToList();
                        cc.Item().PaddingTop(i == 0 ? 3 : 4).Row(chipsRow =>
                        {
                            chipsRow.Spacing(5);
                            foreach (var ic in linha)
                                chipsRow.AutoItem().Element(e => Selo(e, FormatarCategoria(ic), ic.Categoria?.CorHex ?? "#64748B"));
                        });
                    }
                }
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

            if (!string.IsNullOrWhiteSpace(intervencao.NumeroSuporteSiga))
            {
                c.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Column(cc =>
                    {
                        cc.Item().Text("Nº SUPORTE SIGA").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                        cc.Item().Text(intervencao.NumeroSuporteSiga!).FontSize(9.5f).FontColor(Colors.Grey.Darken3);
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

    /// <summary>Desenha a tabela de uma das listas de equipamento (intervencionado, recolhido,
    /// devolvido, abatido ou novo entregue). Quando <paramref name="rotuloColunaExtra"/> é nulo, a
    /// coluna de estado/data não é desenhada (caso do equipamento intervencionado no local e do
    /// novo entregue, que não têm esse conceito). Não desenha "Observações" — cada linha continua a
    /// guardar o seu texto (ver <see cref="LinhaEquipamentoPdf"/>), mas é listado à parte, no fim
    /// do documento (ver <see cref="ComposeObservacoesFinais"/>), para não obrigar esta tabela a
    /// abrir espaço para textos que, a maior parte das vezes, nem estão preenchidos — a decisão de
    /// "Nº Série"/"Descrição" ganharem esse espaço em vez de o deixar por usar.</summary>
    private static void ComposeTabelaEquipamento(
        ColumnDescriptor col, string titulo, string corAccent,
        string? rotuloColunaExtra, IReadOnlyList<LinhaEquipamentoPdf> linhas)
    {
        TituloSeccao(col, titulo, corAccent);

        col.Item().PaddingTop(6).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(1f);
                cols.ConstantColumn(110);
                cols.ConstantColumn(75);
                if (rotuloColunaExtra != null) cols.ConstantColumn(85);
            });

            IContainer CabecalhoCelula(IContainer c) => c.Background(corAccent).PaddingVertical(6).PaddingHorizontal(6).AlignMiddle();

            table.Header(h =>
            {
                h.Cell().Element(CabecalhoCelula).Text("Equipamento").FontSize(8).Bold().FontColor(Colors.White);
                h.Cell().Element(CabecalhoCelula).Text("Nº Série").FontSize(8).Bold().FontColor(Colors.White);
                h.Cell().Element(CabecalhoCelula).Text("Nº Inventário").FontSize(8).Bold().FontColor(Colors.White);
                if (rotuloColunaExtra != null)
                    h.Cell().Element(CabecalhoCelula).Text(rotuloColunaExtra).FontSize(8).Bold().FontColor(Colors.White);
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
            }
        });

        col.Item().PaddingTop(3).Text($"Total: {linhas.Count} equipamento(s)").FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
    }

    /// <summary>Lista, no fim do documento, as observações de todo o equipamento de todas as
    /// tabelas acima que as tiver preenchidas — em vez de uma coluna "Observações" em cada tabela
    /// (que na maior parte das intervenções fica vazia, só a ocupar espaço à custa de "Nº Série"/
    /// "Descrição"), agrupadas aqui por secção de origem, para se continuar a saber a que
    /// equipamento e a que lista cada observação pertence.</summary>
    private static void ComposeObservacoesFinais(ColumnDescriptor col, IReadOnlyList<(string Secao, LinhaEquipamentoPdf Linha)> observacoes)
    {
        if (observacoes.Count == 0) return;

        col.Item().PaddingTop(18);
        TituloSeccao(col, "Observações sobre o Equipamento", CorNavy);

        col.Item().PaddingTop(6).Column(c =>
        {
            c.Spacing(8);
            foreach (var (secao, linha) in observacoes)
            {
                c.Item().Background(CorFundoCaixa).Padding(10).Column(cc =>
                {
                    cc.Item().Text(text =>
                    {
                        text.Span(string.IsNullOrWhiteSpace(linha.Descricao) ? "Equipamento" : linha.Descricao)
                            .FontSize(9.5f).Bold().FontColor(CorNavyEscuro);
                        if (!string.IsNullOrWhiteSpace(linha.NumeroSerie) && linha.NumeroSerie != "-")
                            text.Span($"  ·  Nº Série: {linha.NumeroSerie}").FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                        text.Span($"  ·  {secao}").FontSize(8.5f).Italic().FontColor(Colors.Grey.Darken1);
                    });
                    cc.Item().PaddingTop(3).Text(linha.Observacoes).FontSize(9).FontColor(Colors.Grey.Darken3).LineHeight(1.3f);
                });
            }
        });
    }
}
