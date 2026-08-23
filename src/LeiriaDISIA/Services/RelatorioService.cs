using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LeiriaDISIA.Data;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using OxmlDocument = DocumentFormat.OpenXml.Wordprocessing.Document;
using PdfDocument = QuestPDF.Fluent.Document;

namespace LeiriaDISIA.Services;

/// <summary>
/// Gera o relatório mensal de atividades da DISIA, com a mesma estrutura do
/// relatório modelo fornecido: Sumário, Intervenções Escolares (totais por
/// agrupamento e por categoria), Atividades na DISIA, Reflexão Crítica.
/// </summary>
public partial class RelatorioService
{
    private readonly AppDbContext _db;
    public RelatorioService(AppDbContext db) => _db = db;

    private static readonly string[] NomesMeses =
    {
        "", "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    };

    /// <summary>Descreve o período (ano/mês) escolhido, para usar no título dos relatórios
    /// "Resumo": "Mês de Ano", só "Ano", ou "Todos os Anos" — de forma consistente em toda a
    /// aplicação.</summary>
    private static string DescricaoPeriodo(int? ano, int? mes) =>
        mes is { } m && ano is { } a ? $"{NomesMeses[m]} de {a}"
        : ano is { } anoSo ? anoSo.ToString()
        : "Todos os Anos";

    public string GerarRelatorioMensal(int ano, int mes, string autor, string divisao,
        string telefone, string email, string caminhoDestino)
    {
        var intervencoes = _db.Intervencoes
            .Include(i => i.Escola)
            .Include(i => i.Agrupamento)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .Where(i => i.Ano == ano && i.Mes == mes && i.Estado != EstadoIntervencao.Cancelada)
            .ToList();

        var atividadesDisia = _db.AtividadesDisia
            .Include(a => a.Categoria)
            .Where(a => a.Ano == ano && a.Mes == mes)
            .OrderBy(a => a.Categoria!.Nome).ThenBy(a => a.Data)
            .ToList();

        var porAgrupamento = intervencoes
            .GroupBy(i => i.Agrupamento == null ? "(Sem Agrupamento)" : i.Agrupamento.Nome)
            .Select(g => new { Agrupamento = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToList();

        var porCategoria = intervencoes
            .SelectMany(i => i.Categorias)
            .Where(c => c.Categoria != null)
            .GroupBy(c => c.Categoria!.Nome)
            .Select(g => new { Categoria = g.Key, Total = g.Sum(x => x.Quantidade) })
            .OrderByDescending(g => g.Total)
            .ToList();

        // 2.2: mesmos dados complementares (SIGA + reflexão + imagens) já preenchidos no formulário
        // do Relatório Mensal — dão ao Word o mesmo conteúdo do PDF profissional.
        var dadosSiga = _db.RelatoriosMensaisDados.FirstOrDefault(r => r.Ano == ano && r.Mes == mes)
            ?? new RelatorioMensalDados { Ano = ano, Mes = mes };

        var mesFormatado = $"{NomesMeses[mes]} de {ano}";

        using var doc = WordprocessingDocument.Create(caminhoDestino, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new OxmlDocument();
        var body = mainPart.Document.AppendChild(new Body());

        // Atualiza automaticamente os campos (índice/números de página) quando o documento é aberto,
        // para o utilizador não ter de fazer "Atualizar Campo" manualmente.
        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new Settings(new UpdateFieldsOnOpen { Val = true });
        settingsPart.Settings.Save();

        void Titulo(string texto, int nivel = 1)
        {
            var p = new Paragraph(new Run(new Text(texto)));
            p.ParagraphProperties = new ParagraphProperties(
                new ParagraphStyleId { Val = nivel == 1 ? "Heading1" : "Heading2" });
            body.AppendChild(p);
        }

        // 2.2: cada frase (terminada em ponto final) vira o seu próprio parágrafo, e todos os
        // parágrafos de texto corrido ficam justificados — evita blocos de texto longos e compactos.
        void Paragrafo(string texto)
        {
            foreach (var frase in DividirEmParagrafos(texto))
            {
                body.AppendChild(new Paragraph(
                    new ParagraphProperties(new Justification { Val = JustificationValues.Both },
                        new SpacingBetweenLines { After = "160" }),
                    new Run(new Text(frase) { Space = SpaceProcessingModeValues.Preserve })));
            }
        }

        void ListaItem(string texto)
        {
            body.AppendChild(new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Both }),
                new Run(new Text("• " + texto) { Space = SpaceProcessingModeValues.Preserve })));
        }

        // ---- Capa / cabeçalho ----
        body.AppendChild(new Paragraph(new Run(
            new RunProperties(new Bold(), new FontSize { Val = "36" }),
            new Text("Relatório de Atividades"))));
        body.AppendChild(new Paragraph(new Run(new Text($"{autor} — {divisao}"))));
        body.AppendChild(new Paragraph(new Run(new Text($"Telefone: {telefone}    Email: {email}"))));
        body.AppendChild(new Paragraph(new Run(new RunProperties(new Bold()), new Text(mesFormatado))));
        body.AppendChild(new Paragraph());

        // ---- Índice (campo nativo do Word — números de página reais, atualizados automaticamente) ----
        Titulo("Índice");
        body.AppendChild(new Paragraph(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" TOC \\o \"1-2\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("O índice é atualizado automaticamente ao abrir o documento.")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End })));
        body.AppendChild(new Paragraph(new Break { Type = BreakValues.Page }));

        // ---- Sumário ----
        Titulo("Sumário");
        Paragrafo($"Relatório de atividades desempenhadas no mês de {mesFormatado}, por {autor}, " +
                  $"ao serviço da {divisao}.");

        // ---- Intervenções Escolares ----
        Titulo("Intervenções Escolares");
        Paragrafo($"No âmbito das minhas funções profissionais, assegurei o suporte informático às escolas e " +
                  $"jardins de infância do concelho de Leiria. Foram registadas {intervencoes.Count} " +
                  $"intervenções no mês, com maior incidência nas categorias e agrupamentos detalhados nas " +
                  "tabelas seguintes.");

        Titulo("Total de Intervenções por Agrupamento", 2);
        var tabelaAgrup = CriarTabela(new[] { "Agrupamento", "Total de Intervenções" },
            porAgrupamento.Select(a => new[] { a.Agrupamento, a.Total.ToString() })
                .Append(new[] { "TOTAL", intervencoes.Count.ToString() }));
        body.AppendChild(tabelaAgrup);
        body.AppendChild(new Paragraph());

        Titulo("Totais por Tipo de Intervenção", 2);
        var tabelaCategorias = CriarTabela(new[] { "Categoria", "Total" },
            porCategoria.Select(c => new[] { c.Categoria, c.Total.ToString() }));
        body.AppendChild(tabelaCategorias);
        body.AppendChild(new Paragraph(new Break { Type = BreakValues.Page }));

        // ---- Gestão da Plataforma SIGA ----
        Titulo("Gestão Plataforma SIGA");
        Paragrafo(
            "No âmbito das minhas funções, tenho a responsabilidade pela gestão da plataforma SIGA (Sistema " +
            "Integrado de Gestão e Aprendizagem), utilizada pelas escolas do 1.º Ciclo e Jardins de Infância do " +
            "concelho de Leiria. " +
            "Esta plataforma constitui uma ferramenta fundamental para a gestão e articulação de processos " +
            "educativos, assegurando a comunicação e a interação entre os diversos intervenientes da comunidade " +
            "educativa. " +
            "No exercício desta responsabilidade, asseguro o suporte técnico e funcional da plataforma, " +
            "contribuindo para o correto funcionamento dos serviços e para a eficiência dos processos de " +
            "comunicação e gestão entre todas as entidades envolvidas.");

        Titulo("Atividades na Plataforma SIGA EDUBOX", 2);
        ListaItem($"Gestão dos tickets do processo educativo, incluindo correção de workflows e tipificações " +
                  $"dos pedidos ({dadosSiga.TotalAlteracaoTipificacao} tickets).");
        ListaItem($"Correção dos estados dos tickets ({dadosSiga.TotalEstadoTickets} tickets).");
        ListaItem($"Alteração de palavras-passe ({dadosSiga.TotalAlteracaoPasswords}).");
        body.AppendChild(new Paragraph());

        if (dadosSiga.ImagemPedidosSiga is { Length: > 0 })
            AdicionarImagemCentrada(mainPart, body, dadosSiga.ImagemPedidosSiga, "Figura 1 — Pedidos existentes na Plataforma SIGA.");
        if (dadosSiga.ImagemWorkflowSiga is { Length: > 0 })
            AdicionarImagemCentrada(mainPart, body, dadosSiga.ImagemWorkflowSiga, "Figura 2 — Workflows da Plataforma SIGA.");

        body.AppendChild(new Paragraph(new Break { Type = BreakValues.Page }));

        // ---- Atividades na DISIA ----
        Titulo("Atividades na DISIA");
        Paragrafo($"Durante o mês de {mesFormatado} foram realizadas as seguintes atividades pela DISIA, fora " +
                  "do âmbito direto de uma intervenção numa escola.");
        if (atividadesDisia.Count == 0)
        {
            Paragrafo("Não foram registadas atividades adicionais da DISIA neste mês.");
        }
        else
        {
            foreach (var a in atividadesDisia)
            {
                var sufixo = a.Quantidade > 1 ? $" ({a.Quantidade}x)" : "";
                ListaItem($"[{a.Categoria?.Nome ?? "Sem Categoria"}] {a.Descricao}{sufixo} — {a.Estado}");
            }
        }
        body.AppendChild(new Paragraph(new Break { Type = BreakValues.Page }));

        // ---- Reflexão Crítica ----
        Titulo("Reflexão Crítica");

        void BlocoReflexao(string titulo, string? texto)
        {
            Titulo(titulo, 2);
            Paragrafo(string.IsNullOrWhiteSpace(texto) ? "(Texto não preenchido para este mês.)" : texto);
        }

        BlocoReflexao("Balanço Geral do Mês", dadosSiga.TextoBalancoGeral);
        BlocoReflexao("Principais Desafios e Constrangimentos", dadosSiga.TextoDesafios);
        BlocoReflexao("Propostas de Melhoria para os Próximos Meses", dadosSiga.TextoPropostas);
        BlocoReflexao("Nota Final", dadosSiga.TextoNotaFinal);

        mainPart.Document.Save();
        return caminhoDestino;
    }

    /// <summary>2.2: insere uma imagem centrada na página, seguida de uma legenda centrada por
    /// baixo — usada no relatório Word para as imagens da Plataforma SIGA, mantendo a mesma
    /// disposição visual (imagem + legenda) do relatório PDF. A largura é fixa (16 cm úteis) e a
    /// altura é calculada a partir das dimensões reais da imagem, para preservar a proporção.</summary>
    private static void AdicionarImagemCentrada(MainDocumentPart mainPart, Body body, byte[] bytes, string legenda)
    {
        var tipoImagem = bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 ? ImagePartType.Png : ImagePartType.Jpeg;
        var imagePart = mainPart.AddImagePart(tipoImagem);
        using (var stream = new MemoryStream(bytes))
            imagePart.FeedData(stream);
        var relationshipId = mainPart.GetIdOfPart(imagePart);

        const long larguraEmu = 5486400L; // 16 cm úteis, aproximadamente
        var alturaEmu = larguraEmu;
        try
        {
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap != null && bitmap.Width > 0)
                alturaEmu = larguraEmu * bitmap.Height / bitmap.Width;
        }
        catch
        {
            // Se a imagem não puder ser descodificada para obter a proporção, mantém-se um formato quadrado.
        }

        var elemento = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = larguraEmu, Cy = alturaEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = (UInt32Value)1U, Name = "Imagem" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "Imagem" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = larguraEmu, Cy = alturaEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });

        body.AppendChild(new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(elemento)));

        body.AppendChild(new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { After = "200" }),
            new Run(new RunProperties(new Italic(), new FontSize { Val = "16" }), new Text(legenda))));
    }

    public string GerarRelatorioAnual(int ano, string autor, string divisao,
        string telefone, string email, string caminhoDestino)
    {
        var intervencoes = _db.Intervencoes
            .Include(i => i.Escola)
            .Include(i => i.Agrupamento)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .Where(i => i.Ano == ano)
            .ToList();

        var atividadesDisia = _db.AtividadesDisia.Where(a => a.Ano == ano).ToList();

        var porMes = Enumerable.Range(1, 12)
            .Select(m => new { Mes = m, Total = intervencoes.Count(i => i.Mes == m) })
            .ToList();

        var porAgrupamento = intervencoes
            .GroupBy(i => i.Agrupamento == null ? "(Sem Agrupamento)" : i.Agrupamento.Nome)
            .Select(g => new { Agrupamento = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToList();

        var porEscola = intervencoes
            .GroupBy(i => i.Escola!.Nome)
            .Select(g => new { Escola = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .Take(15)
            .ToList();

        using var doc = WordprocessingDocument.Create(caminhoDestino, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new OxmlDocument();
        var body = mainPart.Document.AppendChild(new Body());

        void Titulo(string texto, int nivel = 1)
        {
            var p = new Paragraph(new Run(new Text(texto)));
            p.ParagraphProperties = new ParagraphProperties(
                new ParagraphStyleId { Val = nivel == 1 ? "Heading1" : "Heading2" });
            body.AppendChild(p);
        }

        void Paragrafo(string texto) => body.AppendChild(new Paragraph(new Run(new Text(texto))));

        body.AppendChild(new Paragraph(new Run(
            new RunProperties(new Bold(), new FontSize { Val = "36" }),
            new Text("Relatório Anual de Atividades"))));
        Paragrafo($"{autor} — {divisao}");
        Paragrafo($"Telefone: {telefone}    Email: {email}");
        Paragrafo($"Ano de {ano}");
        body.AppendChild(new Paragraph());

        Titulo("Sumário");
        Paragrafo($"Relatório anual de atividades desempenhadas em {ano}, por {autor}, ao serviço da {divisao}. " +
                  $"Total de {intervencoes.Count} intervenções nas escolas/JI e {atividadesDisia.Count} atividades no âmbito geral da DISIA.");

        Titulo("Intervenções por Mês");
        var tabelaMeses = CriarTabela(new[] { "Mês", "Total de Intervenções" },
            porMes.Select(m => new[] { NomesMeses[m.Mes], m.Total.ToString() })
                .Append(new[] { "TOTAL", intervencoes.Count.ToString() }));
        body.AppendChild(tabelaMeses);
        body.AppendChild(new Paragraph());

        Titulo("Total de Intervenções por Agrupamento");
        var tabelaAgrup = CriarTabela(new[] { "Agrupamento", "Total" },
            porAgrupamento.Select(a => new[] { a.Agrupamento, a.Total.ToString() }));
        body.AppendChild(tabelaAgrup);
        body.AppendChild(new Paragraph());

        Titulo("Escolas Mais Intervencionadas (Top 15)");
        var tabelaEscolas = CriarTabela(new[] { "Escola", "Total" },
            porEscola.Select(x => new[] { x.Escola, x.Total.ToString() }));
        body.AppendChild(tabelaEscolas);

        Titulo("Nota Final");
        Paragrafo($"Leiria, {DateTime.Today:dd} de {NomesMeses[DateTime.Today.Month]} de {DateTime.Today.Year}.");

        mainPart.Document.Save();
        return caminhoDestino;
    }

    private static Table CriarTabela(string[] cabecalho, IEnumerable<string[]> linhas)
    {
        var table = new Table();
        var props = new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 6 },
                new BottomBorder { Val = BorderValues.Single, Size = 6 },
                new LeftBorder { Val = BorderValues.Single, Size = 6 },
                new RightBorder { Val = BorderValues.Single, Size = 6 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 6 }
            ));
        table.AppendChild(props);

        var headerRow = new TableRow();
        foreach (var h in cabecalho)
        {
            headerRow.AppendChild(new TableCell(
                new TableCellProperties(new Shading { Fill = "1F4E79" }),
                new Paragraph(new Run(new RunProperties(new Bold(), new DocumentFormat.OpenXml.Wordprocessing.Color { Val = "FFFFFF" }), new Text(h)))));
        }
        table.AppendChild(headerRow);

        foreach (var linha in linhas)
        {
            var row = new TableRow();
            foreach (var valor in linha)
                row.AppendChild(new TableCell(new Paragraph(new Run(new Text(valor)))));
            table.AppendChild(row);
        }

        return table;
    }

    // =========================================================================
    // RELATÓRIO: LISTA TOTAL DE ESCOLAS (PDF - QuestPDF)
    // =========================================================================

    public void GerarListaTotalEscolas(string caminhoDestino, int? agrupamentoId = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var query = _db.Escolas
            .Include(e => e.Agrupamento)
            .Where(e => e.Estado != EstadosEscola.Desativada);

        if (agrupamentoId.HasValue && agrupamentoId.Value != 0)
            query = query.Where(e => e.AgrupamentoId == agrupamentoId.Value);

        var escolas = query
            .OrderBy(e => e.Agrupamento == null ? "" : e.Agrupamento.Nome)
            .ThenBy(e => e.Nome)
            .ToList();

        // (4.1) Quando o relatório é filtrado por agrupamento, o título/subtítulo refletem
        // apenas esse agrupamento, em vez do total de todas as escolas.
        var nomeAgrupamentoFiltro = agrupamentoId.HasValue && agrupamentoId.Value != 0
            ? escolas.FirstOrDefault()?.Agrupamento?.Nome
            : null;
        var tituloRelatorio = nomeAgrupamentoFiltro is null
            ? "Lista Total de Escolas e Jardins de Infância"
            : $"Lista de Escolas e Jardins de Infância — {nomeAgrupamentoFiltro}";

        var porAgrupamento = escolas
            .GroupBy(e => e.Agrupamento?.Nome ?? "(Sem Agrupamento)")
            .OrderBy(g => g.Key)
            .ToList();

        var totalEscolas = escolas.Count(e => !IsJardimInfancia(e.Tipo));
        var totalJiIntegrados = escolas.Count(e => IsJardimInfancia(e.Tipo) && e.Integrado);
        var totalJiIsolados = escolas.Count(e => IsJardimInfancia(e.Tipo) && !e.Integrado);
        var totalEdificios = escolas.Count - totalJiIntegrados;

        // Cores corporativas
        var corPrimaria = Colors.Blue.Darken2;    // #1F4E79 approx
        var corAcento = Colors.Blue.Lighten2;
        var corCabecalho = Colors.BlueGrey.Darken3;
        var corFundoAlternado = Colors.Grey.Lighten5;
        var corJi = Colors.Teal.Lighten4;
        var corJiIntegrado = Colors.LightBlue.Lighten4;
        var corTextoSub = Colors.Grey.Darken1;
        var branco = Colors.White;

        PdfDocument.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(9).FontColor(Colors.Grey.Darken3));

                // ---- CABEÇALHO ----
                page.Header().Element(header =>
                {
                    header.Column(col =>
                    {
                        col.Item().PaddingBottom(8).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("MUNICÍPIO DE LEIRIA")
                                    .FontSize(8).FontColor(corTextoSub).LetterSpacing(0.08f);
                                c.Item().Text(tituloRelatorio)
                                    .FontSize(18).Bold().FontColor(corPrimaria);
                                c.Item().Text("DISIA — Divisão de Sistemas de Informação e Aplicações")
                                    .FontSize(9).FontColor(corTextoSub).Italic();
                            });
                            row.ConstantItem(100).AlignRight().Column(c =>
                            {
                                c.Item().AlignRight().Text(DateTime.Today.ToString("dd 'de' MMMM 'de' yyyy",
                                    new System.Globalization.CultureInfo("pt-PT")))
                                    .FontSize(8).FontColor(corTextoSub);
                                c.Item().AlignRight().Text($"Total de registos: {escolas.Count}")
                                    .FontSize(8).FontColor(corTextoSub);
                            });
                            row.ConstantItem(40).PaddingLeft(10).Height(40).Image(AppAssets.LogoDisia).FitArea();
                        });

                        // Linha divisória decorativa
                        col.Item().Height(3).Background(corPrimaria);
                        col.Item().Height(1.5f).Background(corAcento);
                        col.Item().PaddingBottom(4);

                        // Sumário rápido em cards inline
                        col.Item().PaddingBottom(8).Row(row =>
                        {
                            void SummaryCard(RowDescriptor r, string valor, string label, string cor)
                            {
                                r.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2)
                                    .Background(Colors.White).Padding(6).Column(c =>
                                {
                                    c.Item().AlignCenter().Text(valor).FontSize(18).Bold().FontColor(cor);
                                    c.Item().AlignCenter().Text(label).FontSize(7).FontColor(corTextoSub);
                                });
                            }

                            row.ConstantItem(6);
                            SummaryCard(row, totalEdificios.ToString(), "Edifícios", corPrimaria);
                            row.ConstantItem(6);
                            SummaryCard(row, escolas.Count(e => !IsJardimInfancia(e.Tipo)).ToString(), "Escolas (EB/Sec.)", Colors.Green.Darken2);
                            row.ConstantItem(6);
                            SummaryCard(row, totalJiIsolados.ToString(), "JI Isolados", Colors.Orange.Darken2);
                            row.ConstantItem(6);
                            SummaryCard(row, totalJiIntegrados.ToString(), "JI Integrados", Colors.Blue.Medium);
                            row.ConstantItem(6);
                            SummaryCard(row, porAgrupamento.Count.ToString(), "Agrupamentos", Colors.Purple.Darken1);
                            row.ConstantItem(6);
                        });
                    });
                });

                // ---- CONTEÚDO ----
                page.Content().PaddingTop(4).Column(mainCol =>
                {
                    foreach (var grupo in porAgrupamento)
                    {
                        var listaEscolas = grupo.ToList();
                        var nomeAgrupamento = grupo.Key;

                        // Cabeçalho do agrupamento
                        mainCol.Item().PaddingTop(8).PaddingBottom(2).Row(row =>
                        {
                            row.ConstantItem(4).Background(corPrimaria);
                            row.ConstantItem(4);
                            row.RelativeItem().Background(corCabecalho).Padding(6).Row(r =>
                            {
                                r.RelativeItem().Text(nomeAgrupamento)
                                    .FontSize(10).Bold().FontColor(branco);
                                r.ConstantItem(80).AlignRight()
                                    .Text($"{listaEscolas.Count} estabelecimento(s)")
                                    .FontSize(8).FontColor(Colors.Grey.Lighten2);
                            });
                        });

                        // Tabela de escolas deste agrupamento
                        mainCol.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(30);   // Cód.
                                cols.RelativeColumn(3);    // Nome
                                cols.RelativeColumn(1.5f); // Tipo
                                cols.RelativeColumn(2);    // Localidade
                                cols.RelativeColumn(2);    // Freguesia
                                cols.ConstantColumn(22);   // JI Int.
                                cols.ConstantColumn(22);   // Fibra
                                cols.ConstantColumn(22);   // CCTV
                                cols.ConstantColumn(26);   // Biblioteca
                            });

                            // Cabeçalho da tabela
                            static IContainer CellHeader(IContainer c) =>
                                c.Background("#2D4A6A").Padding(4).AlignMiddle();

                            table.Header(h =>
                            {
                                h.Cell().Element(CellHeader).Text("Cód.").FontSize(7).Bold().FontColor(branco);
                                h.Cell().Element(CellHeader).Text("Nome").FontSize(7).Bold().FontColor(branco);
                                h.Cell().Element(CellHeader).Text("Tipo").FontSize(7).Bold().FontColor(branco);
                                h.Cell().Element(CellHeader).Text("Localidade").FontSize(7).Bold().FontColor(branco);
                                h.Cell().Element(CellHeader).Text("Freguesia").FontSize(7).Bold().FontColor(branco);
                                h.Cell().Element(CellHeader).AlignCenter().Text("Integrado").FontSize(6).Bold().FontColor(branco);
                                h.Cell().Element(CellHeader).AlignCenter().Text("Fibra").FontSize(6).Bold().FontColor(branco);
                                h.Cell().Element(CellHeader).AlignCenter().Text("CCTV").FontSize(6).Bold().FontColor(branco);
                                h.Cell().Element(CellHeader).AlignCenter().Text("Biblio.").FontSize(6).Bold().FontColor(branco);
                            });

                            // Linhas
                            for (var i = 0; i < listaEscolas.Count; i++)
                            {
                                var escola = listaEscolas[i];
                                var isJi = IsJardimInfancia(escola.Tipo);
                                var bgRow = isJi
                                    ? (escola.Integrado ? corJiIntegrado : corJi)
                                    : (i % 2 == 0 ? branco : corFundoAlternado);

                                IContainer CellData(IContainer c) =>
                                    c.Background(bgRow).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3)
                                        .Padding(3).AlignMiddle();

                                string Tick(bool val) => val ? "✓" : "—";
                                string TickColor(bool val) => val ? Colors.Green.Darken2 : Colors.Grey.Lighten1;

                                table.Cell().Element(CellData).Text(escola.CodEscola.ToString()).FontSize(7).FontColor(corTextoSub);
                                table.Cell().Element(CellData).Text(escola.Nome).FontSize(8);
                                table.Cell().Element(CellData).Text(escola.Tipo).FontSize(7).FontColor(isJi ? Colors.Teal.Darken2 : corTextoSub).Italic();
                                table.Cell().Element(CellData).Text(escola.Localidade ?? "—").FontSize(7);
                                table.Cell().Element(CellData).Text(escola.Freguesia ?? "—").FontSize(7);
                                table.Cell().Element(CellData).AlignCenter().Text(isJi ? Tick(escola.Integrado) : "—").FontSize(8).Bold().FontColor(isJi ? TickColor(escola.Integrado) : Colors.Grey.Lighten2);
                                table.Cell().Element(CellData).AlignCenter().Text(Tick(escola.TemInternetFibra)).FontSize(8).Bold().FontColor(TickColor(escola.TemInternetFibra));
                                table.Cell().Element(CellData).AlignCenter().Text(Tick(escola.TemCCTV)).FontSize(8).Bold().FontColor(TickColor(escola.TemCCTV));
                                table.Cell().Element(CellData).AlignCenter().Text(Tick(escola.TemBiblioteca)).FontSize(8).Bold().FontColor(TickColor(escola.TemBiblioteca));
                            }
                        });
                    }
                });

                // ---- RODAPÉ ----
                page.Footer().PaddingTop(4).Column(col =>
                {
                    col.Item().Height(1).Background(corAcento);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("DISIA — Município de Leiria  |  Gerado pelo DISIA Manager")
                            .FontSize(7).FontColor(corTextoSub).Italic();
                        row.ConstantItem(100).AlignRight()
                            .Text(t =>
                            {
                                // 2.3: DefaultTextStyle garante tamanho uniforme entre "Pág.", o nº
                                // atual e o total, independentemente do nº de dígitos de cada um.
                                t.DefaultTextStyle(x => x.FontSize(7).FontColor(corTextoSub));
                                t.Span("Pág. ");
                                t.CurrentPageNumber();
                                t.Span(" / ");
                                t.TotalPages();
                            });
                    });
                });
            });
        }).GeneratePdf(caminhoDestino);
    }

    // =========================================================================
    // MOTOR PARTILHADO DOS RELATÓRIOS PDF (usado por todos os relatórios abaixo,
    // exceto a Lista Total de Escolas, que tem uma disposição própria)
    // =========================================================================

    private static readonly string CorTextoPadrao = Colors.Grey.Darken3;

    /// <summary>
    /// Desenha uma fila de "cartões" de resumo (valor grande + rótulo), com a mesma aparência usada
    /// no cabeçalho de <see cref="GerarDocumentoPadrao"/> — mas reutilizável a partir de qualquer
    /// secção do conteúdo de um relatório (ex.: mesmo por cima de um gráfico de barras, dentro do
    /// Relatório Mensal de Atividades). O parâmetro <paramref name="escala"/> permite ajustar o
    /// tamanho dos cartões consoante o espaço disponível na secção onde são inseridos.
    /// </summary>
    private static void DesenharCartoesResumo(ColumnDescriptor col, (string Valor, string Label, string Cor)[] cards, float escala = 1f)
    {
        if (cards.Length == 0) return;

        var branco = Colors.White;
        var corTextoSub = Colors.Grey.Darken1;

        col.Item().PaddingBottom(8).Row(row =>
        {
            void Cartao(RowDescriptor r, string valor, string label, string cor)
            {
                r.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2)
                    .Background(branco).Padding(6 * escala).Column(c =>
                {
                    c.Item().AlignCenter().Text(valor).FontSize(18 * escala).Bold().FontColor(cor);
                    c.Item().AlignCenter().Text(label).FontSize(7 * escala).FontColor(corTextoSub);
                });
            }

            row.ConstantItem(6);
            foreach (var card in cards)
            {
                Cartao(row, card.Valor, card.Label, card.Cor);
                row.ConstantItem(6);
            }
        });
    }

    /// <summary>
    /// Gera um documento PDF A4 com o mesmo "chrome" visual em todos os relatórios: cabeçalho
    /// com título/subtítulo/data/total, faixa de cor decorativa, cartões de resumo opcionais,
    /// e rodapé com paginação. O conteúdo específico de cada relatório é passado em <paramref name="conteudo"/>.
    /// </summary>
    private static void GerarDocumentoPadrao(string caminhoDestino, string tituloRelatorio, string? subtitulo,
        int totalRegistos, (string Valor, string Label, string Cor)[] cards, Action<ColumnDescriptor> conteudo)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var corPrimaria = Colors.Blue.Darken2;
        var corAcento = Colors.Blue.Lighten2;
        var corTextoSub = Colors.Grey.Darken1;
        var branco = Colors.White;

        PdfDocument.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(9).FontColor(CorTextoPadrao));

                page.Header().Element(header =>
                {
                    header.Column(col =>
                    {
                        col.Item().PaddingBottom(8).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("MUNICÍPIO DE LEIRIA")
                                    .FontSize(8).FontColor(corTextoSub).LetterSpacing(0.08f);
                                c.Item().Text(tituloRelatorio)
                                    .FontSize(18).Bold().FontColor(corPrimaria);
                                c.Item().Text("DISIA — Divisão de Sistemas de Informação e Aplicações")
                                    .FontSize(9).FontColor(corTextoSub).Italic();
                            });
                            row.ConstantItem(110).AlignRight().Column(c =>
                            {
                                c.Item().AlignRight().Text(DateTime.Today.ToString("dd 'de' MMMM 'de' yyyy",
                                    new System.Globalization.CultureInfo("pt-PT")))
                                    .FontSize(8).FontColor(corTextoSub);
                                c.Item().AlignRight().Text($"Total de registos: {totalRegistos}")
                                    .FontSize(8).FontColor(corTextoSub);
                            });
                            row.ConstantItem(40).PaddingLeft(10).Height(40).Image(AppAssets.LogoDisia).FitArea();
                        });

                        col.Item().Height(3).Background(corPrimaria);
                        col.Item().Height(1.5f).Background(corAcento);
                        col.Item().PaddingBottom(4);

                        if (!string.IsNullOrWhiteSpace(subtitulo))
                            col.Item().PaddingBottom(6).Text(subtitulo).FontSize(9).FontColor(corTextoSub).Italic();

                        if (cards.Length > 0)
                            DesenharCartoesResumo(col, cards);
                    });
                });

                page.Content().PaddingTop(4).Column(conteudo);

                page.Footer().PaddingTop(4).Column(col =>
                {
                    col.Item().Height(1).Background(corAcento);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("DISIA — Município de Leiria  |  Gerado pelo DISIA Manager")
                            .FontSize(7).FontColor(corTextoSub).Italic();
                        row.ConstantItem(100).AlignRight()
                            .Text(t =>
                            {
                                t.DefaultTextStyle(x => x.FontSize(7).FontColor(corTextoSub));
                                t.Span("Pág. ");
                                t.CurrentPageNumber();
                                t.Span(" / ");
                                t.TotalPages();
                            });
                    });
                });
            });
        }).GeneratePdf(caminhoDestino);
    }

    /// <summary>2.2 / 2.3: divide um texto longo em vários parágrafos — um por cada frase terminada
    /// em ponto final — para que os textos de reflexão/descrição não fiquem num único bloco compacto
    /// e denso, difícil de ler, tanto no PDF como no Word. Preserva quebras de linha já existentes no
    /// texto original (ex.: "\n" entre secções) como separadores de parágrafo adicionais. Frases
    /// muito curtas (ex.: abreviaturas antes de maiúsculas) não são tratadas de forma especial — o
    /// objetivo é legibilidade geral, não uma gramática perfeita.</summary>
    private static IReadOnlyList<string> DividirEmParagrafos(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Array.Empty<string>();

        var blocos = texto.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var paragrafos = new List<string>();

        foreach (var bloco in blocos)
        {
            var frases = System.Text.RegularExpressions.Regex.Split(bloco.Trim(), @"(?<=\.)\s+");
            var atual = "";
            foreach (var frase in frases)
            {
                if (string.IsNullOrWhiteSpace(frase)) continue;
                atual = string.IsNullOrEmpty(atual) ? frase : atual + " " + frase;
                if (frase.TrimEnd().EndsWith('.'))
                {
                    paragrafos.Add(atual.Trim());
                    atual = "";
                }
            }
            if (!string.IsNullOrWhiteSpace(atual)) paragrafos.Add(atual.Trim());
        }

        return paragrafos.Count > 0 ? paragrafos : new[] { texto.Trim() };
    }

    private static IContainer CellHeaderPadrao(IContainer c) => c.Background("#2D4A6A").Padding(4).AlignMiddle();

    private static IContainer CellDadosPadrao(IContainer c, string corFundo) =>
        c.Background(corFundo).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(3).AlignMiddle();

    private static void SemRegistos(ColumnDescriptor col, string mensagem) =>
        col.Item().PaddingVertical(24).AlignCenter().Text(mensagem).FontSize(10).FontColor(Colors.Grey.Darken1).Italic();

    /// <summary>Paleta de cores usada nos gráficos de todos os relatórios "Resumo" — a mesma
    /// paleta usada nos gráficos de barras do Dashboard, para manter a identidade visual entre
    /// a aplicação e os relatórios exportados.</summary>
    private static readonly string[] PaletaGraficos =
    {
        "#1F4E79", "#2AB7CA", "#F59E0B", "#22C55E", "#EF4444",
        "#8B5CF6", "#EC4899", "#14B8A6", "#F97316", "#6366F1",
        "#84CC16", "#0EA5E9", "#D946EF", "#EAB308", "#10B981"
    };

    // Paleta de reserva para categorias de intervenção sem cor própria configurada em "Dados
    // Fixos → Categorias de Intervenção" (CategoriaIntervencao.CorHex) — cores deliberadamente
    // contrastantes para que cada categoria da secção "Tipos de Intervenção por Agrupamento"
    // seja inequívoca, inclusive quando o relatório é impresso.
    private static readonly string[] PaletaCategorias =
    {
        "#1D4ED8", "#D97706", "#15803D", "#B91C1C", "#7E22CE",
        "#0F766E", "#BE185D", "#0369A1", "#A16207", "#4F46E5"
    };

    /// <summary>Desenha um gráfico de barras verticais diretamente no PDF (vetorial, sem gerar
    /// imagens), com uma cor diferente por barra a partir de <see cref="PaletaGraficos"/> — usado
    /// por todos os relatórios "Resumo" para dar uma leitura visual profissional e imediata dos
    /// dados, complementando a tabela detalhada que normalmente se segue.</summary>
    private static void GraficoBarras(ColumnDescriptor col, string titulo, IReadOnlyList<(string Rotulo, int Valor)> dados)
    {
        if (dados.Count == 0) return;

        const float alturaMaxima = 120;
        var maiorValor = Math.Max(1, dados.Max(d => d.Valor));

        col.Item().PaddingTop(6).PaddingBottom(4).Text(titulo).FontSize(11).Bold().FontColor(Colors.Blue.Darken2);
        col.Item().PaddingBottom(10).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Row(row =>
        {
            for (var i = 0; i < dados.Count; i++)
            {
                var (rotulo, valor) = dados[i];
                var cor = PaletaGraficos[i % PaletaGraficos.Length];
                var alturaBarra = Math.Max(2, alturaMaxima * valor / (float)maiorValor);

                row.RelativeItem().PaddingHorizontal(4).Column(barCol =>
                {
                    barCol.Item().AlignCenter().Text(valor.ToString()).FontSize(8).Bold().FontColor(cor);
                    barCol.Item().Height(alturaMaxima - alturaBarra + 2);
                    barCol.Item().Height(alturaBarra).Background(cor);
                    barCol.Item().PaddingTop(3).AlignCenter().Text(rotulo).FontSize(6.5f).FontColor(Colors.Grey.Darken2);
                });
            }
        });
    }

    /// <summary>Desenha um gráfico de barras empilhadas — uma barra por grupo (ex.: agrupamento),
    /// cada uma dividida em segmentos coloridos por série (ex.: tipo de intervenção) — para cruzar
    /// duas dimensões num único gráfico, com legenda de cores por cima.</summary>
    private static void GraficoBarrasEmpilhadas(ColumnDescriptor col, string titulo,
        IReadOnlyList<string> series, IReadOnlyList<(string Grupo, IReadOnlyList<int> Valores)> dados)
    {
        if (dados.Count == 0 || series.Count == 0) return;

        const float alturaMaxima = 150;
        var maiorTotal = Math.Max(1, dados.Max(d => d.Valores.Sum()));

        col.Item().PaddingTop(6).PaddingBottom(4).Text(titulo).FontSize(11).Bold().FontColor(Colors.Blue.Darken2);

        col.Item().PaddingBottom(6).Row(row =>
        {
            for (var s = 0; s < series.Count; s++)
            {
                var cor = PaletaGraficos[s % PaletaGraficos.Length];
                row.AutoItem().PaddingRight(4).PaddingTop(1).Height(9).Width(9).Background(cor);
                row.AutoItem().PaddingRight(12).Text(series[s]).FontSize(6.5f).FontColor(Colors.Grey.Darken2);
            }
        });

        col.Item().PaddingBottom(10).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Row(row =>
        {
            foreach (var (grupo, valores) in dados)
            {
                var total = Math.Max(1, valores.Sum());
                var alturaTotal = Math.Max(2, alturaMaxima * total / (float)maiorTotal);

                row.RelativeItem().PaddingHorizontal(4).Column(barCol =>
                {
                    barCol.Item().AlignCenter().Text(total.ToString()).FontSize(8).Bold().FontColor(Colors.Grey.Darken3);
                    barCol.Item().Height(alturaMaxima - alturaTotal + 2);
                    barCol.Item().Height(alturaTotal).Column(segCol =>
                    {
                        for (var s = 0; s < valores.Count; s++)
                        {
                            if (valores[s] <= 0) continue;
                            var alturaSeg = alturaTotal * valores[s] / (float)total;
                            segCol.Item().Height(alturaSeg).Background(PaletaGraficos[s % PaletaGraficos.Length]);
                        }
                    });
                    barCol.Item().PaddingTop(3).AlignCenter().Text(grupo).FontSize(6.5f).FontColor(Colors.Grey.Darken2);
                });
            }
        });
    }

    /// <summary>Desenha um gráfico circular (pie chart) com legenda de percentagens ao lado —
    /// usado no Relatório Mensal de Atividades. Recorre a desenho SkiaSharp em bruto (ver
    /// <see cref="SkiaSharpHelpers"/>), já que a API fluente do QuestPDF não tem um elemento
    /// nativo equivalente.</summary>
    private static void GraficoPizza(ColumnDescriptor col, string titulo, IReadOnlyList<(string Rotulo, int Valor)> dados)
    {
        var total = dados.Sum(d => d.Valor);
        if (dados.Count == 0 || total <= 0) return;

        col.Item().PaddingTop(6).PaddingBottom(6).Text(titulo).FontSize(11).Bold().FontColor(Colors.Blue.Darken2);

        col.Item().AlignCenter().Row(row =>
        {
            row.ConstantItem(160).Height(160).SkiaSharpSvgCanvas((canvas, size) =>
            {
                var cx = size.Width / 2;
                var cy = size.Height / 2;
                var raio = Math.Min(cx, cy) - 3;
                float anguloInicial = -90;

                for (var i = 0; i < dados.Count; i++)
                {
                    var fatia = 360f * dados[i].Valor / total;
                    if (fatia <= 0) continue;

                    using var paint = new SKPaint
                    {
                        Color = SKColor.Parse(PaletaGraficos[i % PaletaGraficos.Length]),
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill,
                    };
                    using var path = new SKPath();
                    path.MoveTo(cx, cy);
                    path.ArcTo(new SKRect(cx - raio, cy - raio, cx + raio, cy + raio), anguloInicial, fatia, false);
                    path.Close();
                    canvas.DrawPath(path, paint);
                    anguloInicial += fatia;
                }

                using var paintBorda = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
                canvas.DrawCircle(cx, cy, raio, paintBorda);
            });

            row.ConstantItem(230).PaddingLeft(16).Column(legendaCol =>
            {
                for (var i = 0; i < dados.Count; i++)
                {
                    var cor = PaletaGraficos[i % PaletaGraficos.Length];
                    var percentagem = Math.Round(100.0 * dados[i].Valor / total, 1);
                    legendaCol.Item().PaddingBottom(5).Row(itemRow =>
                    {
                        itemRow.ConstantItem(10).Height(10).Background(cor);
                        itemRow.RelativeItem().PaddingLeft(6)
                            .Text($"{dados[i].Rotulo}   {dados[i].Valor} ({percentagem}%)")
                            .FontSize(8.5f).FontColor(Colors.Grey.Darken3);
                    });
                }
            });
        });
    }

    /// <summary>Desenha um gráfico de barras agrupadas: para cada categoria/tipo de intervenção
    /// (eixo X), uma barra por série (agrupamento de escolas), lado a lado — usado para cruzar
    /// duas dimensões (Tipo de Intervenção × Agrupamento) de forma visualmente comparável. O
    /// foco visual é a CATEGORIA, não o agrupamento: todas as barras de uma mesma categoria
    /// partilham a mesma cor (a cor própria da categoria, configurada em "Dados Fixos →
    /// Categorias de Intervenção" — ver <see cref="Models.CategoriaIntervencao.CorHex"/> — ou,
    /// na sua ausência, uma cor de reserva estável derivada do nome da categoria), pelo que a
    /// mesma categoria mantém sempre a mesma cor, mesmo quando existem vários agrupamentos ou a
    /// ordem das categorias muda de mês para mês. Os diferentes agrupamentos dentro de cada grupo
    /// de barras distinguem-se pela legenda por baixo de cada categoria. As barras crescem de
    /// baixo para cima (0 na base), tal como num gráfico de barras convencional. O bloco inteiro
    /// (barras + legenda) é mantido junto na mesma página através de <c>ShowEntire()</c>.</summary>
    private static void GraficoBarrasAgrupadas(ColumnDescriptor col, string titulo,
        IReadOnlyList<string> series, IReadOnlyList<(string Categoria, IReadOnlyList<int> Valores)> dados,
        IReadOnlyDictionary<string, string>? coresPorCategoria = null)
    {
        if (dados.Count == 0 || series.Count == 0) return;

        const float alturaMaxima = 120;
        const float alturaRotuloValor = 10;
        var maiorValor = Math.Max(1, dados.SelectMany(d => d.Valores).DefaultIfEmpty(0).Max());

        // Cor por categoria: usa a cor própria configurada para a categoria (CorHex) quando
        // disponível; caso contrário, cai para a paleta de reserva com base num hash estável do
        // nome, para que a mesma categoria continue sempre com a mesma cor mesmo sem cor
        // configurada e independentemente da ordem em que aparece no gráfico.
        string CorDaCategoria(string categoria)
        {
            if (coresPorCategoria != null && coresPorCategoria.TryGetValue(categoria, out var corConfigurada)
                && !string.IsNullOrWhiteSpace(corConfigurada))
                return corConfigurada;

            var indice = Math.Abs(categoria.GetHashCode()) % PaletaCategorias.Length;
            return PaletaCategorias[indice];
        }

        col.Item().PaddingTop(6).PaddingBottom(4).Text(titulo).FontSize(11).Bold().FontColor(Colors.Blue.Darken2);

        col.Item().ShowEntire().Column(bloco =>
        {
            // ---- Barras: cada categoria com uma barra por série (agrupamento), a crescer de
            // baixo para cima; todas as barras do mesmo grupo usam a cor da categoria ----
            bloco.Item().PaddingBottom(8).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Row(row =>
            {
                foreach (var (categoria, valores) in dados)
                {
                    var cor = CorDaCategoria(categoria);

                    row.RelativeItem().PaddingHorizontal(3).Column(grupoCol =>
                    {
                        grupoCol.Item().Height(alturaMaxima + alturaRotuloValor).Row(barrasRow =>
                        {
                            for (var s = 0; s < valores.Count; s++)
                            {
                                var valor = valores[s];
                                var altura = valor > 0 ? Math.Max(2, alturaMaxima * valor / (float)maiorValor) : 0;

                                barrasRow.RelativeItem().PaddingHorizontal(1).Column(barCol =>
                                {
                                    if (valor > 0)
                                        barCol.Item().Height(alturaRotuloValor).AlignCenter()
                                            .Text(valor.ToString()).FontSize(5.8f).Bold().FontColor(Colors.Grey.Darken3);
                                    barCol.Item().Height(alturaMaxima - altura);
                                    if (valor > 0)
                                        barCol.Item().Height(altura).Background(cor);
                                });
                            }
                        });
                        grupoCol.Item().PaddingTop(3).AlignCenter().Text(categoria).FontSize(7).Bold().FontColor(Colors.Grey.Darken2);
                    });
                }
            });

            // ---- Legenda: correspondência categoria → cor (o foco do gráfico), seguida da
            // lista de agrupamentos representados nas barras de cada grupo (sem cor própria,
            // já que a cor identifica a categoria e não o agrupamento) ----
            bloco.Item().AlignCenter().Row(legendaRow =>
            {
                foreach (var (categoria, _) in dados)
                {
                    var cor = CorDaCategoria(categoria);
                    legendaRow.AutoItem().PaddingRight(4).PaddingTop(1).Height(9).Width(9).Background(cor);
                    legendaRow.AutoItem().PaddingRight(10).Text(categoria).FontSize(6.5f).FontColor(Colors.Grey.Darken2);
                }
            });
            bloco.Item().PaddingTop(3).AlignCenter()
                .Text($"Agrupamentos (ordem das barras em cada grupo): {string.Join(", ", series)}")
                .FontSize(6.5f).Italic().FontColor(Colors.Grey.Darken1);
        });
    }

    // =========================================================================
    // RELATÓRIO: LISTA DE AGRUPAMENTOS (PDF)
    // =========================================================================

    public void GerarListaAgrupamentos(string caminhoDestino)
    {
        var agrupamentos = _db.Agrupamentos
            .Include(a => a.Escolas)
            .OrderBy(a => a.Nome)
            .ToList();

        var totalEscolas = agrupamentos.Sum(a => a.TotalEscolas);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (agrupamentos.Count.ToString(), "Agrupamentos", Colors.Blue.Darken2),
            (totalEscolas.ToString(), "Escolas/JI associadas", Colors.Green.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, "Lista de Agrupamentos",
            "Agrupamentos de escolas do concelho de Leiria e respetivos dados de contacto.",
            agrupamentos.Count, cards, col =>
        {
            if (agrupamentos.Count == 0)
            {
                SemRegistos(col, "Não existem agrupamentos registados.");
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(30);
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(3.5f);
                    cols.RelativeColumn(2.5f);
                    cols.ConstantColumn(45);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Cód.").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Nome").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Morada").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Contacto / Email").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Escolas").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < agrupamentos.Count; i++)
                {
                    var a = agrupamentos[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(a.CodAgrupamento.ToString()).FontSize(7).FontColor(Colors.Grey.Darken1);
                    table.Cell().Element(Cell).Text(a.Nome).FontSize(8).Bold();
                    table.Cell().Element(Cell).Text(a.Morada ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text($"{a.Contacto1 ?? "—"}   {a.Email1 ?? ""}").FontSize(7.5f);
                    table.Cell().Element(Cell).AlignCenter().Text(a.TotalEscolas.ToString()).FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: CÓDIGOS GEPE (PDF) — 5.2.1
    // =========================================================================

    public void GerarListaCodigosGepe(string caminhoDestino)
    {
        var escolas = _db.Escolas
            .Include(e => e.Agrupamento)
            .Where(e => e.Estado != EstadosEscola.Desativada)
            .OrderBy(e => e.Agrupamento == null ? "" : e.Agrupamento.Nome)
            .ThenBy(e => e.Nome)
            .ToList();

        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (escolas.Count.ToString(), "Escolas / JI", Colors.Blue.Darken2),
            (escolas.Count(e => e.CodGEPE.HasValue).ToString(), "Com Código GEPE", Colors.Green.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, "Códigos GEPE",
            "Código da escola, nome, agrupamento e código GEPE de cada estabelecimento ativo.",
            escolas.Count, cards, col =>
        {
            if (escolas.Count == 0)
            {
                SemRegistos(col, "Não existem escolas registadas.");
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(45);   // Código da Escola
                    cols.RelativeColumn(3);    // Nome da Escola
                    cols.RelativeColumn(2.5f); // Agrupamento
                    cols.ConstantColumn(70);   // Código GEPE
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Cód. Escola").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Nome da Escola").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Agrupamento").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Código GEPE").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < escolas.Count; i++)
                {
                    var e = escolas[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(e.CodEscola).FontSize(7).FontColor(Colors.Grey.Darken1);
                    table.Cell().Element(Cell).Text(e.Nome).FontSize(8).Bold();
                    table.Cell().Element(Cell).Text(e.Agrupamento?.Nome ?? "(Sem Agrupamento)").FontSize(7.5f);
                    table.Cell().Element(Cell).AlignCenter()
                        .Text(e.CodGEPE?.ToString() ?? "—").FontSize(8)
                        .FontColor(e.CodGEPE.HasValue ? Colors.Blue.Darken2 : Colors.Grey.Lighten1);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: RESUMO DE INFRAESTRUTURA TECNOLÓGICA (PDF, com gráfico)
    // =========================================================================

    public void GerarResumoInfraestrutura(string caminhoDestino)
    {
        var escolas = _db.Escolas.Include(e => e.Agrupamento).Where(e => e.Estado != EstadosEscola.Desativada).ToList();
        var total = escolas.Count;

        var comFibra = escolas.Count(e => e.TemInternetFibra);
        var comCctv = escolas.Count(e => e.TemCCTV);
        var comVpn = escolas.Count(e => e.TemVPN);

        var porAgrupamento = escolas
            .GroupBy(e => e.Agrupamento?.Nome ?? "(Sem Agrupamento)")
            .Select(g => new
            {
                Agrupamento = g.Key,
                Total = g.Count(),
                Fibra = g.Count(x => x.TemInternetFibra),
                Cctv = g.Count(x => x.TemCCTV),
                Vpn = g.Count(x => x.TemVPN)
            })
            .OrderByDescending(g => g.Total)
            .ToList();

        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (total.ToString(), "Escolas Ativas", Colors.Blue.Darken2),
            (total == 0 ? "0%" : $"{comFibra * 100 / total}%", "Com Fibra", Colors.Green.Darken2),
            (total == 0 ? "0%" : $"{comCctv * 100 / total}%", "Com CCTV", Colors.Orange.Darken2),
            (total == 0 ? "0%" : $"{comVpn * 100 / total}%", "Com VPN", Colors.Purple.Darken1),
        };

        GerarDocumentoPadrao(caminhoDestino, "Resumo de Infraestrutura Tecnológica",
            "Cobertura de fibra, CCTV e VPN nas escolas e jardins de infância ativos, por agrupamento.",
            total, cards, col =>
        {
            if (total == 0)
            {
                SemRegistos(col, "Não existem escolas ativas registadas.");
                return;
            }

            GraficoBarras(col, "Escolas com Fibra por Agrupamento", porAgrupamento.Select(a => (a.Agrupamento, a.Fibra)).ToList());

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3f);
                    cols.ConstantColumn(45);
                    cols.ConstantColumn(45);
                    cols.ConstantColumn(45);
                    cols.ConstantColumn(45);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Agrupamento").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Escolas").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Fibra").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("CCTV").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("VPN").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < porAgrupamento.Count; i++)
                {
                    var a = porAgrupamento[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(a.Agrupamento).FontSize(8);
                    table.Cell().Element(Cell).AlignCenter().Text(a.Total.ToString()).FontSize(8).Bold();
                    table.Cell().Element(Cell).AlignCenter().Text(a.Fibra.ToString()).FontSize(7.5f).FontColor(Colors.Green.Darken2);
                    table.Cell().Element(Cell).AlignCenter().Text(a.Cctv.ToString()).FontSize(7.5f).FontColor(Colors.Orange.Darken2);
                    table.Cell().Element(Cell).AlignCenter().Text(a.Vpn.ToString()).FontSize(7.5f).FontColor(Colors.Purple.Darken1);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: LISTA TOTAL DE INTERVENÇÕES (PDF)
    // =========================================================================

    /// <summary>Gera a lista de intervenções. Se <paramref name="dataInicio"/> e/ou
    /// <paramref name="dataFim"/> forem indicados, filtra por esse período (usado pelos botões
    /// "Mês Corrente" e "Período à Escolha"); caso contrário, usa o filtro por <paramref name="ano"/>
    /// como antes (ou nenhum filtro, para a lista total).</summary>
    public void GerarListaIntervencoes(string caminhoDestino, int? ano = null,
        DateTime? dataInicio = null, DateTime? dataFim = null)
    {
        var query = _db.Intervencoes
            .Include(i => i.Escola)
            .Include(i => i.Agrupamento)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .AsQueryable();

        var usaPeriodo = dataInicio.HasValue || dataFim.HasValue;
        if (usaPeriodo)
        {
            if (dataInicio is { } di) query = query.Where(i => i.Data >= di.Date);
            if (dataFim is { } df) query = query.Where(i => i.Data <= df.Date);
        }
        else if (ano is { } anoFiltro)
        {
            query = query.Where(i => i.Ano == anoFiltro);
        }

        var intervencoes = query.OrderByDescending(i => i.Data).ToList();

        var fechadas = intervencoes.Count(i => i.Estado == EstadoIntervencao.Fechada);
        var pendentes = intervencoes.Count(i => i.Estado == EstadoIntervencao.Pendente);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (intervencoes.Count.ToString(), ano is { } a ? $"Intervenções em {a}" : "Total de Intervenções", Colors.Blue.Darken2),
            (fechadas.ToString(), "Fechadas", Colors.Green.Darken2),
            (pendentes.ToString(), "Pendentes", Colors.Red.Darken1),
        };

        var titulo = usaPeriodo
            ? $"Lista de Intervenções — {dataInicio?.ToString("dd/MM/yyyy") ?? "início"} a {dataFim?.ToString("dd/MM/yyyy") ?? "hoje"}"
            : ano is { } anoTitulo ? $"Lista de Intervenções — {anoTitulo}" : "Lista Total de Intervenções";

        GerarDocumentoPadrao(caminhoDestino, titulo,
            "Registo de intervenções técnicas realizadas nas escolas e jardins de infância.",
            intervencoes.Count, cards, col =>
        {
            if (intervencoes.Count == 0)
            {
                SemRegistos(col, "Não existem intervenções registadas para o período selecionado.");
                return;
            }

            // Resumo visual por agrupamento primeiro (visão geral), com o detalhe linha-a-linha
            // logo a seguir — ordem habitual num relatório profissional (resumo → detalhe).
            // Intervenções "Canceladas" não contam aqui, tal como nos totais do Dashboard.
            var porAgrupamentoResumo = intervencoes
                .Where(i => i.Estado != EstadoIntervencao.Cancelada)
                .GroupBy(i => i.Agrupamento?.Nome ?? "(Sem Agrupamento)")
                .Select(g => new { Agrupamento = g.Key, Total = g.Count() })
                .OrderByDescending(g => g.Total)
                .ToList();

            if (porAgrupamentoResumo.Count > 0)
            {
                GraficoBarras(col, "Resumo por Agrupamento (excl. Canceladas)",
                    porAgrupamentoResumo.Select(a => (a.Agrupamento, a.Total)).ToList());
            }

            col.Item().PaddingTop(porAgrupamentoResumo.Count > 0 ? 4 : 0).PaddingBottom(4)
                .Text("Detalhe das Intervenções").FontSize(11).Bold().FontColor(Colors.Blue.Darken2);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(48);
                    cols.RelativeColumn(2.3f);
                    cols.RelativeColumn(3.5f);
                    cols.RelativeColumn(2);
                    cols.ConstantColumn(58);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Data").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Escola").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Descrição").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Categorias").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Estado").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < intervencoes.Count; i++)
                {
                    var it = intervencoes[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);
                    var categorias = string.Join(", ", it.Categorias.Select(cat => cat.Categoria?.Nome).Where(n => n != null));

                    table.Cell().Element(Cell).Text(it.Data.ToString("dd-MM-yyyy")).FontSize(7);
                    table.Cell().Element(Cell).Text(it.Escola?.Nome ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(it.Descricao).FontSize(7.5f);
                    table.Cell().Element(Cell).Text(string.IsNullOrEmpty(categorias) ? "—" : categorias).FontSize(7).FontColor(Colors.Grey.Darken1);
                    table.Cell().Element(Cell).AlignCenter().Text(it.Estado.ToString()).FontSize(7)
                        .FontColor(it.Estado == EstadoIntervencao.Fechada ? Colors.Green.Darken2
                            : it.Estado == EstadoIntervencao.Pendente ? Colors.Red.Darken1 : Colors.Orange.Darken2);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: RESUMO DE INTERVENÇÕES POR AGRUPAMENTO (PDF, com gráfico)
    // =========================================================================

    /// <summary>Resumo visual das intervenções técnicas agrupadas por agrupamento de escolas,
    /// com gráfico de barras e tabela de detalhe por estado. Intervenções "Canceladas" não contam,
    /// porque não chegaram a ser executadas (mesma regra usada no Dashboard).</summary>
    public void GerarResumoIntervencoesPorAgrupamento(string caminhoDestino, int? ano = null, int? mes = null)
    {
        var query = _db.Intervencoes.Include(i => i.Agrupamento)
            .Where(i => i.Estado != EstadoIntervencao.Cancelada).AsQueryable();
        if (mes is { } mesFiltro && ano is { } anoFiltro2) query = query.Where(i => i.Ano == anoFiltro2 && i.Mes == mesFiltro);
        else if (ano is { } anoFiltro) query = query.Where(i => i.Ano == anoFiltro);
        var intervencoes = query.ToList();

        var porAgrupamento = intervencoes
            .GroupBy(i => i.Agrupamento?.Nome ?? "(Sem Agrupamento)")
            .Select(g => new
            {
                Agrupamento = g.Key,
                Total = g.Count(),
                Fechadas = g.Count(x => x.Estado == EstadoIntervencao.Fechada),
                Pendentes = g.Count(x => x.Estado == EstadoIntervencao.Pendente),
                EmProgresso = g.Count(x => x.Estado == EstadoIntervencao.EmProgresso),
                EmEspera = g.Count(x => x.Estado == EstadoIntervencao.EmEspera)
            })
            .OrderByDescending(g => g.Total)
            .ToList();

        var maisIntervencionado = porAgrupamento.FirstOrDefault();
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;
        var periodo = DescricaoPeriodo(ano, mes);

        var cards = new (string, string, string)[]
        {
            (intervencoes.Count.ToString(), $"Intervenções Válidas — {periodo}", Colors.Blue.Darken2),
            (porAgrupamento.Count.ToString(), "Agrupamentos Envolvidos", Colors.Purple.Darken1),
            (maisIntervencionado?.Total.ToString() ?? "0", $"Mais intervencionado: {maisIntervencionado?.Agrupamento ?? "—"}", Colors.Orange.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, $"Resumo de Intervenções por Agrupamento — {periodo}",
            "Distribuição das intervenções técnicas por agrupamento de escolas (intervenções canceladas não contam, pois não chegaram a ser executadas).",
            intervencoes.Count, cards, col =>
        {
            if (porAgrupamento.Count == 0)
            {
                SemRegistos(col, "Não existem intervenções registadas para o período selecionado.");
                return;
            }

            GraficoBarras(col, "Intervenções por Agrupamento", porAgrupamento.Select(a => (a.Agrupamento, a.Total)).ToList());

            col.Item().PaddingTop(6).Table(table =>
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

                for (var i = 0; i < porAgrupamento.Count; i++)
                {
                    var a = porAgrupamento[i];
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
        });
    }

    // =========================================================================
    // RELATÓRIO: RESUMO DE INTERVENÇÕES POR CATEGORIA (PDF, com gráfico)
    // =========================================================================

    public void GerarResumoIntervencoesPorCategoria(string caminhoDestino, int? ano = null, int? mes = null)
    {
        var query = _db.Intervencoes.Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .Where(i => i.Estado != EstadoIntervencao.Cancelada).AsQueryable();
        if (mes is { } mesFiltro && ano is { } anoFiltro2) query = query.Where(i => i.Ano == anoFiltro2 && i.Mes == mesFiltro);
        else if (ano is { } anoFiltro) query = query.Where(i => i.Ano == anoFiltro);
        var intervencoes = query.ToList();

        var porCategoria = intervencoes
            .SelectMany(i => i.Categorias)
            .Where(c => c.Categoria != null)
            .GroupBy(c => c.Categoria!.Nome)
            .Select(g => new { Categoria = g.Key, Total = g.Sum(x => x.Quantidade) })
            .OrderByDescending(g => g.Total)
            .ToList();

        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;
        var periodo = DescricaoPeriodo(ano, mes);

        var cards = new (string, string, string)[]
        {
            (intervencoes.Count.ToString(), $"Intervenções Válidas — {periodo}", Colors.Blue.Darken2),
            (porCategoria.Count.ToString(), "Categorias Distintas", Colors.Purple.Darken1),
            (porCategoria.FirstOrDefault()?.Total.ToString() ?? "0", $"Mais comum: {porCategoria.FirstOrDefault()?.Categoria ?? "—"}", Colors.Teal.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, $"Resumo de Intervenções por Categoria — {periodo}",
            "Distribuição das intervenções técnicas por categoria (intervenções canceladas não contam, pois não chegaram a ser executadas).",
            intervencoes.Count, cards, col =>
        {
            if (porCategoria.Count == 0)
            {
                SemRegistos(col, "Não existem intervenções com categorias associadas para o período selecionado.");
                return;
            }

            GraficoBarras(col, "Intervenções por Categoria", porCategoria.Select(c => (c.Categoria, c.Total)).ToList());

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(4);
                    cols.ConstantColumn(70);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Categoria").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Total").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < porCategoria.Count; i++)
                {
                    var c = porCategoria[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer ct) => CellDadosPadrao(ct, bg);

                    table.Cell().Element(Cell).Text(c.Categoria).FontSize(8);
                    table.Cell().Element(Cell).AlignCenter().Text(c.Total.ToString()).FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: RESUMO DE INTERVENÇÕES POR TIPO E AGRUPAMENTO (PDF, gráfico empilhado)
    // =========================================================================

    /// <summary>Cruza duas dimensões — tipo (categoria) de intervenção e agrupamento de escolas —
    /// num único gráfico de barras empilhadas, para se perceber visualmente onde se concentra cada
    /// tipo de intervenção. Intervenções "Canceladas" não contam. Só as 8 categorias mais frequentes
    /// aparecem individualizadas (as restantes somam-se em "Outras"), para o gráfico e a tabela se
    /// manterem legíveis; use o "Resumo por Categoria" para o detalhe completo por categoria.</summary>
    public void GerarResumoIntervencoesPorTipoAgrupamento(string caminhoDestino, int? ano = null, int? mes = null)
    {
        var query = _db.Intervencoes
            .Include(i => i.Agrupamento)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .Where(i => i.Estado != EstadoIntervencao.Cancelada).AsQueryable();
        if (mes is { } mesFiltro && ano is { } anoFiltro2) query = query.Where(i => i.Ano == anoFiltro2 && i.Mes == mesFiltro);
        else if (ano is { } anoFiltro) query = query.Where(i => i.Ano == anoFiltro);
        var intervencoes = query.ToList();

        var categoriasOrdenadas = intervencoes
            .SelectMany(i => i.Categorias)
            .Where(c => c.Categoria != null)
            .GroupBy(c => c.Categoria!.Nome)
            .OrderByDescending(g => g.Sum(x => x.Quantidade))
            .Select(g => g.Key)
            .ToList();

        const int maxCategoriasGrafico = 8;
        var categoriasGrafico = categoriasOrdenadas.Take(maxCategoriasGrafico).ToList();
        var temOutras = categoriasOrdenadas.Count > maxCategoriasGrafico;
        var seriesLegenda = temOutras ? categoriasGrafico.Append("Outras").ToList() : categoriasGrafico;

        var porAgrupamento = intervencoes
            .GroupBy(i => i.Agrupamento?.Nome ?? "(Sem Agrupamento)")
            .Select(g =>
            {
                var porCategoriaDoGrupo = g.SelectMany(i => i.Categorias)
                    .Where(c => c.Categoria != null)
                    .GroupBy(c => c.Categoria!.Nome)
                    .ToDictionary(cg => cg.Key, cg => cg.Sum(x => x.Quantidade));

                var valores = categoriasGrafico.Select(cat => porCategoriaDoGrupo.GetValueOrDefault(cat, 0)).ToList();
                if (temOutras)
                {
                    var outras = porCategoriaDoGrupo.Where(kv => !categoriasGrafico.Contains(kv.Key)).Sum(kv => kv.Value);
                    valores.Add(outras);
                }

                return new { Agrupamento = g.Key, Total = g.Count(), Valores = valores };
            })
            .OrderByDescending(g => g.Total)
            .ToList();

        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;
        var periodo = DescricaoPeriodo(ano, mes);

        var cards = new (string, string, string)[]
        {
            (intervencoes.Count.ToString(), $"Intervenções Válidas — {periodo}", Colors.Blue.Darken2),
            (porAgrupamento.Count.ToString(), "Agrupamentos", Colors.Purple.Darken1),
            (categoriasOrdenadas.Count.ToString(), "Tipos de Intervenção", Colors.Teal.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, $"Resumo de Intervenções por Tipo e Agrupamento — {periodo}",
            "Cruzamento entre o tipo (categoria) de intervenção e o agrupamento de escolas (intervenções canceladas não contam, pois não chegaram a ser executadas).",
            intervencoes.Count, cards, col =>
        {
            if (porAgrupamento.Count == 0 || categoriasOrdenadas.Count == 0)
            {
                SemRegistos(col, "Não existem intervenções com categorias associadas para o período selecionado.");
                return;
            }

            GraficoBarrasEmpilhadas(col, "Tipos de Intervenção por Agrupamento", seriesLegenda,
                porAgrupamento.Select(a => (a.Agrupamento, (IReadOnlyList<int>)a.Valores)).ToList());

            if (temOutras)
                col.Item().PaddingBottom(6).Text($"\"Outras\" agrega {categoriasOrdenadas.Count - maxCategoriasGrafico} categoria(s) menos frequentes.")
                    .FontSize(7).Italic().FontColor(Colors.Grey.Darken1);

            col.Item().PaddingTop(2).Text("Detalhe por Agrupamento e Tipo").FontSize(11).Bold().FontColor(Colors.Blue.Darken2);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(2.2f);
                    foreach (var _ in seriesLegenda) cols.RelativeColumn(1);
                    cols.ConstantColumn(40);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Agrupamento").FontSize(6.3f).Bold().FontColor(branco);
                    foreach (var cat in seriesLegenda)
                        h.Cell().Element(CellHeaderPadrao).AlignCenter().Text(cat).FontSize(5.6f).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Total").FontSize(6.3f).Bold().FontColor(branco);
                });

                for (var i = 0; i < porAgrupamento.Count; i++)
                {
                    var a = porAgrupamento[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(a.Agrupamento).FontSize(7).Bold();
                    foreach (var valor in a.Valores)
                        table.Cell().Element(Cell).AlignCenter().Text(valor == 0 ? "—" : valor.ToString()).FontSize(6.8f)
                            .FontColor(valor == 0 ? Colors.Grey.Lighten1 : Colors.Grey.Darken2);
                    table.Cell().Element(Cell).AlignCenter().Text(a.Total.ToString()).FontSize(7).Bold().FontColor(Colors.Blue.Darken2);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: LISTA DE PEDIDOS DE INTERVENÇÃO (PDF)
    // =========================================================================

    public void GerarListaPedidosIntervencao(string caminhoDestino)
    {
        var pedidos = _db.PedidosIntervencao
            .Include(p => p.Escola)
            .OrderByDescending(p => p.DataPedido)
            .ToList();

        var pendentes = pedidos.Count(p => p.EstaEmAberto);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (pedidos.Count.ToString(), "Total de Pedidos", Colors.Blue.Darken2),
            (pendentes.ToString(), "Em Aberto", Colors.Red.Darken1),
            ((pedidos.Count - pendentes).ToString(), "Concluídos/Cancelados", Colors.Green.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, "Lista de Pedidos de Intervenção",
            "Pedidos de intervenção submetidos pelas escolas, antes de serem convertidos (ou não) em intervenções.",
            pedidos.Count, cards, col =>
        {
            if (pedidos.Count == 0)
            {
                SemRegistos(col, "Não existem pedidos de intervenção registados.");
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(48);
                    cols.RelativeColumn(2.3f);
                    cols.RelativeColumn(1.8f);
                    cols.RelativeColumn(3.5f);
                    cols.ConstantColumn(32);
                    cols.ConstantColumn(60);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Data").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Escola").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Solicitante").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Razão").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Dias").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Estado").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < pedidos.Count; i++)
                {
                    var p = pedidos[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(p.DataPedido.ToString("dd-MM-yyyy")).FontSize(7);
                    table.Cell().Element(Cell).Text(p.Escola?.Nome ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(p.Solicitante).FontSize(7.5f);
                    table.Cell().Element(Cell).Text(p.Razao).FontSize(7.5f);
                    table.Cell().Element(Cell).AlignCenter().Text(p.DiasEmAberto.ToString()).FontSize(7)
                        .FontColor(p.EstaEmAberto ? Colors.Red.Darken1 : Colors.Grey.Darken1);
                    table.Cell().Element(Cell).AlignCenter().Text(p.Estado.ToString()).FontSize(7)
                        .FontColor(p.EstaEmAberto ? Colors.Orange.Darken2 : Colors.Green.Darken2);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: RESUMO DE PEDIDOS DE INTERVENÇÃO POR ESTADO (PDF, com gráfico)
    // =========================================================================

    public void GerarResumoPedidosPorEstado(string caminhoDestino)
    {
        var pedidos = _db.PedidosIntervencao.Include(p => p.Escola).ToList();

        var porEstado = pedidos
            .GroupBy(p => p.Estado)
            .Select(g => new { Estado = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToList();

        var porEscola = pedidos
            .GroupBy(p => p.Escola?.Nome ?? "(Sem Escola)")
            .Select(g => new { Escola = g.Key, Total = g.Count(), EmAberto = g.Count(x => x.EstaEmAberto) })
            .OrderByDescending(g => g.Total)
            .Take(15)
            .ToList();

        var emAberto = pedidos.Count(p => p.EstaEmAberto);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (pedidos.Count.ToString(), "Total de Pedidos", Colors.Blue.Darken2),
            (emAberto.ToString(), "Em Aberto", Colors.Red.Darken1),
            ((pedidos.Count - emAberto).ToString(), "Resolvidos", Colors.Green.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, "Resumo de Pedidos de Intervenção",
            "Distribuição dos pedidos de intervenção por estado, e escolas com mais pedidos submetidos.",
            pedidos.Count, cards, col =>
        {
            if (pedidos.Count == 0)
            {
                SemRegistos(col, "Não existem pedidos de intervenção registados.");
                return;
            }

            GraficoBarras(col, "Pedidos por Estado", porEstado.Select(e => (e.Estado.ToString(), e.Total)).ToList());

            col.Item().PaddingTop(6).Text("Escolas com Mais Pedidos Submetidos").FontSize(11).Bold().FontColor(Colors.Blue.Darken2);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3.4f);
                    cols.ConstantColumn(55);
                    cols.ConstantColumn(60);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Escola").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Total").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Em Aberto").FontSize(6.5f).Bold().FontColor(branco);
                });

                for (var i = 0; i < porEscola.Count; i++)
                {
                    var e = porEscola[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(e.Escola).FontSize(8);
                    table.Cell().Element(Cell).AlignCenter().Text(e.Total.ToString()).FontSize(8).Bold();
                    table.Cell().Element(Cell).AlignCenter().Text(e.EmAberto.ToString()).FontSize(7.5f)
                        .FontColor(e.EmAberto > 0 ? Colors.Red.Darken1 : Colors.Grey.Lighten1);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: LISTA DE ATIVIDADES DA DISIA (PDF)
    // =========================================================================

    public void GerarListaAtividadesDisia(string caminhoDestino, int? ano = null)
    {
        var query = _db.AtividadesDisia.Include(a => a.Categoria).AsQueryable();
        if (ano is { } anoFiltro) query = query.Where(a => a.Ano == anoFiltro);

        var atividades = query.OrderByDescending(a => a.Data).ToList();
        var totalServicos = atividades.Sum(a => a.Quantidade);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (atividades.Count.ToString(), "Atividades Registadas", Colors.Blue.Darken2),
            (totalServicos.ToString(), "Total de Serviços Prestados", Colors.Purple.Darken1),
        };

        GerarDocumentoPadrao(caminhoDestino, ano is { } anoTitulo ? $"Atividades da DISIA — {anoTitulo}" : "Lista de Atividades da DISIA",
            "Atividades desempenhadas pela DISIA fora do âmbito escolar direto (juntas de freguesia, instalações municipais, etc.).",
            atividades.Count, cards, col =>
        {
            if (atividades.Count == 0)
            {
                SemRegistos(col, "Não existem atividades da DISIA registadas.");
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(48);
                    cols.RelativeColumn(4);
                    cols.RelativeColumn(1.8f);
                    cols.RelativeColumn(2.2f);
                    cols.ConstantColumn(22);
                    cols.ConstantColumn(60);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Data").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Descrição").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Categoria").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Local").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Qtd.").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Estado").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < atividades.Count; i++)
                {
                    var a = atividades[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(a.Data.ToString("dd-MM-yyyy")).FontSize(7);
                    table.Cell().Element(Cell).Text(a.Descricao).FontSize(7.5f);
                    table.Cell().Element(Cell).Text(a.Categoria?.Nome ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(a.Local ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).AlignCenter().Text(a.Quantidade.ToString()).FontSize(7.5f).Bold();
                    table.Cell().Element(Cell).AlignCenter().Text(a.Estado.ToString()).FontSize(7)
                        .FontColor(a.Estado == EstadoIntervencao.Fechada ? Colors.Green.Darken2 : Colors.Orange.Darken2);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: RESUMO DE ATIVIDADES DA DISIA POR CATEGORIA (PDF, com gráfico)
    // =========================================================================

    public void GerarResumoAtividadesDisiaPorCategoria(string caminhoDestino, int? ano = null, int? mes = null)
    {
        var query = _db.AtividadesDisia.Include(a => a.Categoria).AsQueryable();
        if (mes is { } mesFiltro && ano is { } anoFiltro2) query = query.Where(a => a.Ano == anoFiltro2 && a.Mes == mesFiltro);
        else if (ano is { } anoFiltro) query = query.Where(a => a.Ano == anoFiltro);
        var atividades = query.ToList();

        var porCategoria = atividades
            .GroupBy(a => a.Categoria?.Nome ?? "(Sem Categoria)")
            .Select(g => new { Categoria = g.Key, Total = g.Count(), Servicos = g.Sum(x => x.Quantidade) })
            .OrderByDescending(g => g.Servicos)
            .ToList();

        var fechadas = atividades.Count(a => a.Estado == EstadoIntervencao.Fechada);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;
        var periodo = DescricaoPeriodo(ano, mes);

        var cards = new (string, string, string)[]
        {
            (atividades.Count.ToString(), $"Atividades — {periodo}", Colors.Blue.Darken2),
            (fechadas.ToString(), "Fechadas", Colors.Green.Darken2),
            (porCategoria.Count.ToString(), "Categorias Distintas", Colors.Purple.Darken1),
        };

        GerarDocumentoPadrao(caminhoDestino, $"Resumo de Atividades DISIA por Categoria — {periodo}",
            "Distribuição das atividades da DISIA (fora do âmbito escolar direto) por categoria.",
            atividades.Count, cards, col =>
        {
            if (porCategoria.Count == 0)
            {
                SemRegistos(col, "Não existem atividades da DISIA registadas para o período selecionado.");
                return;
            }

            GraficoBarras(col, "Serviços Prestados por Categoria", porCategoria.Select(c => (c.Categoria, c.Servicos)).ToList());

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3.4f);
                    cols.ConstantColumn(65);
                    cols.ConstantColumn(65);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Categoria").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Atividades").FontSize(6.5f).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Serviços").FontSize(6.5f).Bold().FontColor(branco);
                });

                for (var i = 0; i < porCategoria.Count; i++)
                {
                    var c = porCategoria[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer ct) => CellDadosPadrao(ct, bg);

                    table.Cell().Element(Cell).Text(c.Categoria).FontSize(8);
                    table.Cell().Element(Cell).AlignCenter().Text(c.Total.ToString()).FontSize(8);
                    table.Cell().Element(Cell).AlignCenter().Text(c.Servicos.ToString()).FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: LISTA DE EQUIPAMENTO INFORMÁTICO (PDF)
    // =========================================================================

    /// <summary>Inventário de equipamento informático, agrupado por Agrupamento → Escola, com
    /// subtotais (incluindo detalhe por nível de obsolescência) no final de cada escola e de cada
    /// agrupamento, e um total geral no fim. Equipamento sem escola associada (instalações
    /// municipais, etc.) aparece à parte, em "Outros Locais", no final do documento.</summary>
    public void GerarListaEquipamento(string caminhoDestino)
    {
        var equipamentos = _db.Equipamentos
            .Include(e => e.Escola).ThenInclude(esc => esc!.Agrupamento)
            .ToList();

        var obsoletos = equipamentos.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.Obsoleto);
        var monitorizar = equipamentos.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.AMonitorizar);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        const string corBandaAgrupamento = "#2D4A6A";
        const string corBandaEscola = "#D6E4F0";
        const string corSubtotalEscola = "#EDEDED";
        const string corSubtotalAgrupamento = "#BFD7EA";
        const string corTotalGeral = "#1F4E79";

        var cards = new (string, string, string)[]
        {
            (equipamentos.Count.ToString(), "Total de Equipamento", Colors.Blue.Darken2),
            (monitorizar.ToString(), "A Monitorizar", Colors.Orange.Darken2),
            (obsoletos.ToString(), "Obsoleto", Colors.Red.Darken1),
        };

        // Agrupar: Agrupamento → Escola → Equipamento. O que não tem escola associada (instalações
        // municipais, juntas de freguesia, etc.) fica à parte, em "Outros Locais", no final.
        var comEscola = equipamentos.Where(e => e.Escola != null).ToList();
        var semEscola = equipamentos.Where(e => e.Escola == null)
            .OrderBy(e => e.LocalNaoEscolar).ThenBy(e => e.Tipo).ToList();

        var porAgrupamento = comEscola
            .GroupBy(e => e.Escola!.Agrupamento?.Nome ?? "(Sem Agrupamento)")
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Agrupamento = g.Key,
                Escolas = g.GroupBy(e => e.Escola!)
                    .OrderBy(eg => eg.Key.Nome)
                    .Select(eg => new { Escola = eg.Key, Itens = eg.OrderBy(x => x.Tipo).ThenBy(x => x.Marca).ToList() })
                    .ToList()
            })
            .ToList();

        GerarDocumentoPadrao(caminhoDestino, "Lista de Equipamentos - Inventário",
            "Inventário de equipamento informático, agrupado por Agrupamento e Escola, com classificação de obsolescência (ver Administração → Obsolescência).",
            equipamentos.Count, cards, col =>
        {
            if (equipamentos.Count == 0)
            {
                SemRegistos(col, "Não existe equipamento registado.");
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1.6f);
                    cols.RelativeColumn(2.2f);
                    cols.RelativeColumn(2.6f);
                    cols.RelativeColumn(2.2f);
                    cols.ConstantColumn(60);
                    cols.ConstantColumn(70);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Cód. GEPE").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Tipo").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Marca/Modelo").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Localização").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Estado").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Obsolescência").FontSize(6.5f).Bold().FontColor(branco);
                });

                var indiceLinha = 0;

                string ResumoNiveis(IReadOnlyList<Equipamento> itens)
                {
                    var atual = itens.Count(x => x.Obsolescencia.Nivel == NivelObsolescencia.Atual);
                    var monit = itens.Count(x => x.Obsolescencia.Nivel == NivelObsolescencia.AMonitorizar);
                    var obs = itens.Count(x => x.Obsolescencia.Nivel == NivelObsolescencia.Obsoleto);
                    return $"Atual: {atual} · A Monitorizar: {monit} · Obsoleto: {obs}";
                }

                foreach (var agr in porAgrupamento)
                {
                    table.Cell().ColumnSpan(6).Element(c => c.Background(corBandaAgrupamento).Padding(5))
                        .Text($"AGRUPAMENTO: {agr.Agrupamento}").FontSize(9).Bold().FontColor(branco);

                    foreach (var esc in agr.Escolas)
                    {
                        var codGepeTexto = esc.Escola.CodGEPE?.ToString() ?? "—";

                        table.Cell().ColumnSpan(6).Element(c => c.Background(corBandaEscola).Padding(4))
                            .Text($"Escola: {esc.Escola.Nome}   ·   Cód. GEPE: {codGepeTexto}").FontSize(8).Bold().FontColor(Colors.Blue.Darken2);

                        foreach (var eq in esc.Itens)
                        {
                            var bg = indiceLinha % 2 == 0 ? branco : corFundoAlternado;
                            IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);
                            var obsEq = eq.Obsolescencia;

                            table.Cell().Element(Cell).Text(codGepeTexto).FontSize(7.5f);
                            table.Cell().Element(Cell).Text(eq.Tipo).FontSize(7.5f);
                            table.Cell().Element(Cell).Text($"{eq.Marca} {eq.Modelo}".Trim()).FontSize(7.5f);
                            table.Cell().Element(Cell).Text("—").FontSize(7.5f).FontColor(Colors.Grey.Lighten1);
                            table.Cell().Element(Cell).AlignCenter().Text(eq.Estado).FontSize(7);
                            table.Cell().Element(Cell).AlignCenter().Text(obsEq.Classificacao).FontSize(7).Bold().FontColor(obsEq.CorHex);
                            indiceLinha++;
                        }

                        table.Cell().ColumnSpan(6).Element(c => c.Background(corSubtotalEscola).Padding(4))
                            .Text($"Subtotal {esc.Escola.Nome}: {esc.Itens.Count} equipamento(s)   ({ResumoNiveis(esc.Itens)})")
                            .FontSize(7).Bold().FontColor(Colors.Grey.Darken2);
                    }

                    var itensAgrupamento = agr.Escolas.SelectMany(e => e.Itens).ToList();
                    table.Cell().ColumnSpan(6).Element(c => c.Background(corSubtotalAgrupamento).Padding(5))
                        .Text($"SUBTOTAL {agr.Agrupamento}: {itensAgrupamento.Count} equipamento(s)   ({ResumoNiveis(itensAgrupamento)})")
                        .FontSize(7.5f).Bold().FontColor(Colors.Blue.Darken2);
                }

                if (semEscola.Count > 0)
                {
                    table.Cell().ColumnSpan(6).Element(c => c.Background(corBandaAgrupamento).Padding(5))
                        .Text("OUTROS LOCAIS (NÃO ESCOLARES)").FontSize(9).Bold().FontColor(branco);

                    foreach (var eq in semEscola)
                    {
                        var bg = indiceLinha % 2 == 0 ? branco : corFundoAlternado;
                        IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);
                        var obsEq = eq.Obsolescencia;

                        table.Cell().Element(Cell).Text("—").FontSize(7.5f).FontColor(Colors.Grey.Lighten1);
                        table.Cell().Element(Cell).Text(eq.Tipo).FontSize(7.5f);
                        table.Cell().Element(Cell).Text($"{eq.Marca} {eq.Modelo}".Trim()).FontSize(7.5f);
                        table.Cell().Element(Cell).Text(eq.LocalNaoEscolar ?? "—").FontSize(7.5f);
                        table.Cell().Element(Cell).AlignCenter().Text(eq.Estado).FontSize(7);
                        table.Cell().Element(Cell).AlignCenter().Text(obsEq.Classificacao).FontSize(7).Bold().FontColor(obsEq.CorHex);
                        indiceLinha++;
                    }

                    table.Cell().ColumnSpan(6).Element(c => c.Background(corSubtotalEscola).Padding(4))
                        .Text($"Subtotal Outros Locais: {semEscola.Count} equipamento(s)   ({ResumoNiveis(semEscola)})")
                        .FontSize(7).Bold().FontColor(Colors.Grey.Darken2);
                }

                table.Cell().ColumnSpan(6).Element(c => c.Background(corTotalGeral).Padding(6))
                    .Text($"TOTAL GERAL: {equipamentos.Count} equipamento(s)   ({ResumoNiveis(equipamentos)})")
                    .FontSize(9).Bold().FontColor(branco);
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: RESUMO DE OBSOLESCÊNCIA DO PARQUE INFORMÁTICO (PDF, com gráfico)
    // =========================================================================

    public void GerarResumoObsolescencia(string caminhoDestino)
    {
        var equipamentos = _db.Equipamentos.Include(e => e.Escola).ToList();

        var porNivel = equipamentos
            .GroupBy(e => e.Obsolescencia.Classificacao)
            .Select(g => new { Nivel = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToList();

        var porTipo = equipamentos
            .GroupBy(e => e.Tipo)
            .Select(g => new
            {
                Tipo = g.Key,
                Total = g.Count(),
                Atual = g.Count(x => x.Obsolescencia.Nivel == NivelObsolescencia.Atual),
                Monitorizar = g.Count(x => x.Obsolescencia.Nivel == NivelObsolescencia.AMonitorizar),
                Obsoleto = g.Count(x => x.Obsolescencia.Nivel == NivelObsolescencia.Obsoleto)
            })
            .OrderByDescending(g => g.Total)
            .ToList();

        var obsoletos = equipamentos.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.Obsoleto);
        var monitorizar = equipamentos.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.AMonitorizar);
        var atual = equipamentos.Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.Atual);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (equipamentos.Count.ToString(), "Total de Equipamento", Colors.Blue.Darken2),
            (atual.ToString(), "Atual", Colors.Green.Darken2),
            (monitorizar.ToString(), "A Monitorizar", Colors.Orange.Darken2),
            (obsoletos.ToString(), "Obsoleto", Colors.Red.Darken1),
        };

        GerarDocumentoPadrao(caminhoDestino, "Resumo de Obsolescência do Parque Informático",
            "Classificação de obsolescência de todo o equipamento informático inventariado, por tipo de equipamento (ver critérios em Administração → Obsolescência).",
            equipamentos.Count, cards, col =>
        {
            if (equipamentos.Count == 0)
            {
                SemRegistos(col, "Não existe equipamento registado.");
                return;
            }

            GraficoBarras(col, "Equipamento por Nível de Obsolescência", porNivel.Select(n => (n.Nivel, n.Total)).ToList());

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(2.6f);
                    cols.ConstantColumn(45);
                    cols.ConstantColumn(50);
                    cols.ConstantColumn(65);
                    cols.ConstantColumn(55);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Tipo").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Total").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Atual").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("A Monit.").FontSize(6.5f).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Obsoleto").FontSize(6.5f).Bold().FontColor(branco);
                });

                for (var i = 0; i < porTipo.Count; i++)
                {
                    var t = porTipo[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(t.Tipo).FontSize(8);
                    table.Cell().Element(Cell).AlignCenter().Text(t.Total.ToString()).FontSize(8).Bold();
                    table.Cell().Element(Cell).AlignCenter().Text(t.Atual.ToString()).FontSize(7.5f).FontColor(Colors.Green.Darken2);
                    table.Cell().Element(Cell).AlignCenter().Text(t.Monitorizar.ToString()).FontSize(7.5f).FontColor(Colors.Orange.Darken2);
                    table.Cell().Element(Cell).AlignCenter().Text(t.Obsoleto.ToString()).FontSize(7.5f).FontColor(Colors.Red.Darken1);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: LISTA DE EQUIPAMENTO ABATIDO (PDF)
    // =========================================================================

    public void GerarListaEquipamentoAbatido(string caminhoDestino)
    {
        var abatidos = _db.EquipamentosAbatidos
            .Include(a => a.Equipamento)
            .OrderByDescending(a => a.DataAbate)
            .ToList();

        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (abatidos.Count.ToString(), "Total Abatido", Colors.Blue.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, "Lista de Equipamento Abatido",
            "Registo histórico de equipamento informático abatido, doado ou reciclado.",
            abatidos.Count, cards, col =>
        {
            if (abatidos.Count == 0)
            {
                SemRegistos(col, "Não existe equipamento abatido registado.");
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(48);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2.4f);
                    cols.RelativeColumn(3.4f);
                    cols.ConstantColumn(70);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Data").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Nº Série / Inventário").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Escola/Local").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Descrição").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Status").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < abatidos.Count; i++)
                {
                    var a = abatidos[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);
                    var serie = a.Equipamento?.NumeroSerie ?? a.NumeroSerie;
                    var inventario = a.Equipamento?.NumeroInventario ?? a.NumeroInventario;
                    var numeroSerie = (serie, inventario) switch
                    {
                        (not null, not null) => $"{serie} / {inventario}",
                        (not null, null) => serie,
                        (null, not null) => inventario,
                        _ => "—"
                    };
                    var localTexto = a.Equipamento?.Escola?.Nome ?? a.EscolaOuLocal ?? "—";
                    var descricao = a.DescricaoEquipamento ?? "—";

                    table.Cell().Element(Cell).Text(a.DataAbate.ToString("dd-MM-yyyy")).FontSize(7);
                    table.Cell().Element(Cell).Text(numeroSerie).FontSize(7.5f);
                    table.Cell().Element(Cell).Text(localTexto).FontSize(7.5f);
                    table.Cell().Element(Cell).Text(descricao).FontSize(7.5f);
                    table.Cell().Element(Cell).AlignCenter().Text(a.Status).FontSize(7).Bold().FontColor(Colors.Red.Darken1);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: LISTA DE EQUIPAMENTO RECOLHIDO (PDF)
    // =========================================================================

    public void GerarListaEquipamentoRecolhido(string caminhoDestino)
    {
        var recolhidos = _db.EquipamentosRecolhidos
            .Include(r => r.Equipamento).ThenInclude(e => e!.Escola)
            .OrderByDescending(r => r.DataRecolha)
            .ToList();

        var pendentes = recolhidos.Count(r => !r.EstaEntregue);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (recolhidos.Count.ToString(), "Total de Recolhas", Colors.Blue.Darken2),
            (pendentes.ToString(), "Ainda Fora da Escola", Colors.Orange.Darken2),
            ((recolhidos.Count - pendentes).ToString(), "Já Entregues", Colors.Green.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, "Lista de Equipamento Recolhido",
            "Equipamento recolhido das escolas para intervenção nas instalações da DISIA.",
            recolhidos.Count, cards, col =>
        {
            if (recolhidos.Count == 0)
            {
                SemRegistos(col, "Não existem recolhas registadas.");
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(48);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2.4f);
                    cols.ConstantColumn(28);
                    cols.ConstantColumn(70);
                    cols.ConstantColumn(48);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Recolhido em").FontSize(6.5f).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Nº Série").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Escola").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Dias").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Estado").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Entregue em").FontSize(6.5f).Bold().FontColor(branco);
                });

                for (var i = 0; i < recolhidos.Count; i++)
                {
                    var r = recolhidos[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(r.DataRecolha.ToString("dd-MM-yyyy")).FontSize(7);
                    table.Cell().Element(Cell).Text(r.Equipamento?.NumeroSerie ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(r.Equipamento?.Escola?.Nome ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).AlignCenter().Text(r.DiasEmRecolha.ToString()).FontSize(7)
                        .FontColor(r.EstaEntregue ? Colors.Grey.Darken1 : Colors.Orange.Darken2);
                    table.Cell().Element(Cell).AlignCenter().Text(r.Estado).FontSize(7).Bold()
                        .FontColor(r.EstaEntregue ? Colors.Green.Darken2 : Colors.Orange.Darken2);
                    table.Cell().Element(Cell).Text(r.DataEntrega?.ToString("dd-MM-yyyy") ?? "—").FontSize(7);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: LISTA DE LIGAÇÕES DE COMUNICAÇÕES (PDF)
    // =========================================================================

    public void GerarListaComunicacoes(string caminhoDestino)
    {
        var ligacoes = _db.Comunicacoes
            .Include(c => c.Escola)
            .OrderBy(c => c.Escola!.Nome)
            .ToList();

        var integradas = ligacoes.Count(c => c.Integrado);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (ligacoes.Count.ToString(), "Total de Ligações", Colors.Blue.Darken2),
            (integradas.ToString(), "Integradas na DISIA", Colors.Green.Darken2),
            ((ligacoes.Count - integradas).ToString(), "Por Integrar", Colors.Orange.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, "Lista de Ligações de Comunicações",
            "Ligações de comunicações (fibra e outras) associadas às escolas e jardins de infância.",
            ligacoes.Count, cards, col =>
        {
            if (ligacoes.Count == 0)
            {
                SemRegistos(col, "Não existem ligações de comunicações registadas.");
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(1.6f);
                    cols.RelativeColumn(1.4f);
                    cols.RelativeColumn(1.8f);
                    cols.ConstantColumn(45);
                    cols.ConstantColumn(65);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Escola").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Tipo").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Velocidade").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Operadora").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Integr.").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Estado").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < ligacoes.Count; i++)
                {
                    var c = ligacoes[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer ct) => CellDadosPadrao(ct, bg);

                    table.Cell().Element(Cell).Text(c.Escola?.Nome ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(c.TipoLigacao).FontSize(7.5f);
                    table.Cell().Element(Cell).Text(c.VelocidadeFibra ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(c.Operadora ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).AlignCenter().Text(c.Integrado ? "✓" : "—").FontSize(8).Bold()
                        .FontColor(c.Integrado ? Colors.Green.Darken2 : Colors.Grey.Lighten1);
                    table.Cell().Element(Cell).AlignCenter().Text(c.Estado).FontSize(7);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: RESUMO DE COMUNICAÇÕES POR ESTADO E OPERADORA (PDF, com gráfico)
    // =========================================================================

    public void GerarResumoComunicacoesPorEstado(string caminhoDestino)
    {
        var ligacoes = _db.Comunicacoes.Include(c => c.Escola).ToList();

        var porEstado = ligacoes
            .GroupBy(c => c.Estado)
            .Select(g => new { Estado = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToList();

        var porOperadora = ligacoes
            .GroupBy(c => c.Operadora ?? "(Sem Operadora)")
            .Select(g => new { Operadora = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToList();

        var integradas = ligacoes.Count(c => c.Integrado);
        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (ligacoes.Count.ToString(), "Total de Ligações", Colors.Blue.Darken2),
            (integradas.ToString(), "Integradas", Colors.Green.Darken2),
            (porOperadora.Count.ToString(), "Operadoras Distintas", Colors.Purple.Darken1),
        };

        GerarDocumentoPadrao(caminhoDestino, "Resumo de Comunicações",
            "Distribuição das ligações de comunicações por operadora e por estado de integração.",
            ligacoes.Count, cards, col =>
        {
            if (ligacoes.Count == 0)
            {
                SemRegistos(col, "Não existem ligações de comunicações registadas.");
                return;
            }

            GraficoBarras(col, "Ligações por Operadora", porOperadora.Select(o => (o.Operadora, o.Total)).ToList());

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3.4f);
                    cols.ConstantColumn(70);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Estado").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).AlignCenter().Text("Total").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < porEstado.Count; i++)
                {
                    var e = porEstado[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer c) => CellDadosPadrao(c, bg);

                    table.Cell().Element(Cell).Text(e.Estado).FontSize(8);
                    table.Cell().Element(Cell).AlignCenter().Text(e.Total.ToString()).FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                }
            });
        });
    }

    // =========================================================================
    // RELATÓRIO: LISTA DE CONTACTOS (PDF)
    // =========================================================================

    public void GerarListaContactos(string caminhoDestino)
    {
        var contactos = _db.Contactos
            .Include(c => c.Escola)
            .OrderBy(c => c.Escola == null ? "" : c.Escola.Nome).ThenBy(c => c.Nome)
            .ToList();

        var branco = Colors.White;
        var corFundoAlternado = Colors.Grey.Lighten5;

        var cards = new (string, string, string)[]
        {
            (contactos.Count.ToString(), "Total de Contactos", Colors.Blue.Darken2),
        };

        GerarDocumentoPadrao(caminhoDestino, "Lista de Contactos",
            "Contactos associados às escolas e jardins de infância (funcionários, professores, coordenadores).",
            contactos.Count, cards, col =>
        {
            if (contactos.Count == 0)
            {
                SemRegistos(col, "Não existem contactos registados.");
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(2.2f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(2.2f);
                    cols.RelativeColumn(2.3f);
                    cols.RelativeColumn(1.4f);
                    cols.RelativeColumn(1.4f);
                });

                table.Header(h =>
                {
                    h.Cell().Element(CellHeaderPadrao).Text("Nome").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Função").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Escola / Entidade").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Email").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Telefone").FontSize(7).Bold().FontColor(branco);
                    h.Cell().Element(CellHeaderPadrao).Text("Telemóvel").FontSize(7).Bold().FontColor(branco);
                });

                for (var i = 0; i < contactos.Count; i++)
                {
                    var c = contactos[i];
                    var bg = i % 2 == 0 ? branco : corFundoAlternado;
                    IContainer Cell(IContainer ct) => CellDadosPadrao(ct, bg);

                    table.Cell().Element(Cell).Text(c.Nome).FontSize(7.5f).Bold();
                    table.Cell().Element(Cell).Text(c.Funcao ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(c.Escola?.Nome ?? c.EntidadeExterna ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(c.Email ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(c.Telefone ?? "—").FontSize(7.5f);
                    table.Cell().Element(Cell).Text(c.Telemovel ?? "—").FontSize(7.5f);
                }
            });
        });
    }

    /// <summary>Nº de linhas mínimas da tabela quando a folha é gerada em branco (ver
    /// <see cref="GerarFolhaInventarioPdf"/> para o desenho completo do PDF).</summary>
    private const int LinhasMinimasFolhaInventario = 22;

    /// <summary>Ponto de entrada "de conveniência" que faz a consulta à base de dados e depois
    /// desenha o PDF, tudo na mesma thread onde for chamado. Só deve ser usado a partir da UI
    /// thread com datasets pequenos (ex.: chamadas futuras fora de um diálogo com barra de
    /// progresso). O diálogo "Folha de Inventário" da aplicação NÃO usa este método diretamente —
    /// separa a consulta (rápida, na UI thread) do desenho do PDF (mais lento, numa thread em
    /// segundo plano via <see cref="GerarFolhaInventarioPdf"/>), porque o <c>AppDbContext</c> do
    /// Entity Framework Core não é thread-safe e não pode ser acedido a partir de uma Task.Run
    /// enquanto a app continua a correr na UI thread.</summary>
    public void GerarFolhaInventario(string caminhoDestino, int? escolaId, string? responsavel, bool preFilled)
    {
        var (escola, equipamentos) = ObterDadosFolhaInventario(escolaId, preFilled);
        GerarFolhaInventarioPdf(caminhoDestino, escola, equipamentos, responsavel);
    }

    /// <summary>Parte "consulta à base de dados" da Folha de Inventário — rápida (uma escola + a
    /// respetiva lista de equipamento, no máximo algumas centenas de linhas), por isso é segura de
    /// correr diretamente na UI thread, antes de entregar o desenho do PDF (esse sim, mais lento) a
    /// uma thread em segundo plano.</summary>
    public (Escola? Escola, List<Equipamento> Equipamentos) ObterDadosFolhaInventario(int? escolaId, bool preFilled)
    {
        var escola = escolaId.HasValue
            ? _db.Escolas.Include(e => e.Agrupamento).FirstOrDefault(e => e.Id == escolaId.Value)
            : null;

        var equipamentos = preFilled && escola != null
            ? _db.Equipamentos
                .Where(e => e.EscolaId == escola.Id && e.Estado != EstadosEquipamento.Abatido)
                .OrderBy(e => e.Tipo).ThenBy(e => e.Marca).ThenBy(e => e.NumeroSerie)
                .ToList()
            : new List<Equipamento>();

        return (escola, equipamentos);
    }

    /// <summary>
    /// Item 3.3: Folha de Inventário de Equipamentos Informáticos, em PDF A4 paisagem, destinada a
    /// impressão e preenchimento presencial numa escola (para levantamento manual do parque
    /// informático existente no local).
    ///
    /// Paisagem (não retrato) porque as tabelas têm várias colunas — em retrato ficariam demasiado
    /// estreitas para escrita manual legível; a orientação larga dá mais área útil de preenchimento.
    ///
    /// Uma tabela por categoria de equipamento (Computadores, Monitores, Equipamento de Rede,
    /// Impressoras, Outros Equipamentos), tal como pedido — a folha pode facilmente ocupar 2
    /// páginas, o que não é problema (o cabeçalho de cada tabela repete-se automaticamente em cada
    /// página nova, via <c>table.Header(...)</c>). Cada equipamento é colocado na tabela certa
    /// através do mesmo "grupo de características" já usado em Inserir/Editar Equipamento (ver
    /// <see cref="ObterGrupoCaracteristicas"/>, que replica fielmente
    /// Views/EquipamentoEditWindow.ObterGrupoCaracteristicas):
    /// - Computadores: PC de Secretária, Portátil, Servidor.
    /// - Monitores: Monitor.
    /// - Equipamento de Rede: Switch, Router, Access Point.
    /// - Impressoras: Impressora, Multifunções.
    /// - Outros Equipamentos: tudo o resto (Câmara CCTV, Projetor, Quadro Interativo, Tablet,
    ///   UPS/No-break, Telefone IP, "Outro", e quaisquer tipos novos que o administrador venha a
    ///   criar sem indicar um grupo de características específico).
    ///
    /// As 4 tabelas de categoria mostram as CARACTERÍSTICAS ESPECÍFICAS desse grupo como colunas
    /// verdadeiras (uma coluna por característica), lidas diretamente da configuração em
    /// Administração → Dados Fixos → Tipos de Equipamento → (grupo) — exatamente as mesmas
    /// características que aparecem em "Características Adicionais"/painéis fixos ao editar um
    /// equipamento (ver <see cref="CaracteristicaEquipamento"/>), incluindo as que o administrador
    /// venha a acrescentar no futuro (a tabela adapta-se sozinha, sem precisar de alterar código).
    /// Por pedido explícito, a tabela de Computadores NÃO mostra Localização/Sala, Estado nem
    /// Observações; a tabela "Outros Equipamentos" mantém essas colunas (não têm características
    /// específicas com esquema conhecido, por serem tipos muito heterogéneos).
    ///
    /// Duas variantes, decididas pelo chamador através do conteúdo de <paramref name="equipamentos"/>
    /// (ver <see cref="ObterDadosFolhaInventario"/>): em branco (lista vazia — cada tabela mostra um
    /// nº mínimo de linhas vazias) ou pré-preenchida (uma linha por equipamento existente, com os
    /// valores já gravados, incluindo os das características específicas — a pessoa no local só
    /// precisa de confirmar/corrigir). Se uma escola tiver menos equipamento de uma categoria do que
    /// o mínimo de linhas, as linhas a mais ficam em branco (para registar equipamento novo
    /// encontrado no local mas ainda não cadastrado).
    ///
    /// Este método acede à base de dados só para ler a CONFIGURAÇÃO de características (tabela
    /// pequena, praticamente estática) — nunca a equipamento/escolas, que já vêm em
    /// <paramref name="equipamentos"/>/<paramref name="escola"/> (ver <see cref="ObterDadosFolhaInventario"/>).
    /// Por isso continua seguro de chamar numa thread em segundo plano (Task.Run).
    /// </summary>
    public void GerarFolhaInventarioPdf(string caminhoDestino, Escola? escola, List<Equipamento> equipamentos, string? responsavel)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var corPrimaria = Colors.Blue.Darken2;
        var corAcento = Colors.Blue.Lighten2;
        var corTextoSub = Colors.Grey.Darken1;
        var branco = Colors.White;

        // ---- Categorização do equipamento (mesma lógica de Views/EquipamentoEditWindow) ----
        // GroupBy+First (em vez de ToDictionary direto) para nunca rebentar caso alguma vez existam
        // dois "Tipos de Equipamento" com o mesmo nome em Dados Fixos (não há restrição de
        // unicidade na base de dados a impedi-lo).
        var grupoPorTipo = _db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.TipoEquipamento)
            .ToList()
            .GroupBy(v => v.Valor)
            .ToDictionary(g => g.Key, g => g.First().GrupoCaracteristicas);

        var porGrupo = equipamentos
            .GroupBy(e => ObterGrupoCaracteristicas(e.Tipo, grupoPorTipo))
            .ToDictionary(g => g.Key, g => g.ToList());

        List<Equipamento> DoGrupo(string grupo) => porGrupo.TryGetValue(grupo, out var lista) ? lista : new List<Equipamento>();

        var computadores = DoGrupo(GruposCaracteristicasEquipamento.Computador);
        var monitores = DoGrupo(GruposCaracteristicasEquipamento.Monitor);
        var rede = DoGrupo(GruposCaracteristicasEquipamento.Rede);
        var impressoras = DoGrupo(GruposCaracteristicasEquipamento.Impressora);
        var outros = equipamentos.Except(computadores).Except(monitores).Except(rede).Except(impressoras).ToList();

        // ---- Características específicas configuradas em Dados Fixos, por grupo ----
        // Todas as ativas de topo (sem características-filha isoladas — seguem sempre a
        // característica-pai, tal como no formulário de Inserir/Editar Equipamento), incluindo as
        // que já têm campo fixo próprio (Processador, Nº de Portas, etc.) — aqui, ao contrário do
        // painel "Características Adicionais" do formulário, é suposto aparecerem TODAS como coluna.
        List<CaracteristicaEquipamento> CaracteristicasDoGrupo(string grupo) => _db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == grupo && c.Ativo && c.CaracteristicaPaiId == null)
            .OrderBy(c => c.Ordem).ThenBy(c => c.Nome)
            .ToList();

        var caracteristicasComputador = CaracteristicasDoGrupo(GruposCaracteristicasEquipamento.Computador);
        var caracteristicasMonitor = CaracteristicasDoGrupo(GruposCaracteristicasEquipamento.Monitor);
        var caracteristicasRede = CaracteristicasDoGrupo(GruposCaracteristicasEquipamento.Rede);
        var caracteristicasImpressora = CaracteristicasDoGrupo(GruposCaracteristicasEquipamento.Impressora);

        // Valores já gravados de características SEM campo fixo próprio (ex.: uma característica
        // extra criada livremente pelo administrador) — para as que têm campo fixo (Processador,
        // etc.), o valor vem diretamente da propriedade correspondente em Equipamento, através de
        // ResolvedoresCaracteristicasEmbutidas, sem precisar de consultar esta tabela.
        var idsEquipamento = equipamentos.Select(e => e.Id).ToList();
        var valoresDinamicos = idsEquipamento.Count == 0
            ? new Dictionary<(int, int), string?>()
            : _db.EquipamentoCaracteristicaValores
                .Where(v => idsEquipamento.Contains(v.EquipamentoId))
                .ToList()
                .GroupBy(v => (v.EquipamentoId, v.CaracteristicaEquipamentoId))
                .ToDictionary(g => g.Key, g => g.First().Valor);

        string? ValorCaracteristica(Equipamento eq, CaracteristicaEquipamento car) =>
            ResolvedoresCaracteristicasEmbutidas.TryGetValue(car.Nome, out var resolvedor)
                ? resolvedor(eq)
                : valoresDinamicos.GetValueOrDefault((eq.Id, car.Id));

        PdfDocument.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(9).FontColor(Colors.Grey.Darken3));

                // ---- CABEÇALHO ----
                page.Header().Element(header =>
                {
                    header.Column(col =>
                    {
                        col.Item().PaddingBottom(6).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("MUNICÍPIO DE LEIRIA").FontSize(8).FontColor(corTextoSub).LetterSpacing(0.08f);
                                c.Item().Text("Folha de Inventário de Equipamentos Informáticos")
                                    .FontSize(16).Bold().FontColor(corPrimaria);
                                c.Item().Text("DISIA — Divisão de Sistemas de Informação e Aplicações")
                                    .FontSize(8).FontColor(corTextoSub).Italic();
                            });
                            row.ConstantItem(36).Height(36).Image(AppAssets.LogoDisia).FitArea();
                        });

                        col.Item().Height(2.5f).Background(corPrimaria);
                        col.Item().Height(1.2f).Background(corAcento);

                        // Campos de identificação: valor real quando disponível (escola/agrupamento
                        // escolhidos, responsável indicado no diálogo de geração), senão uma linha
                        // para preenchimento manual — a Data fica sempre em branco propositadamente,
                        // já que a folha pode ser impressa num dia e usada presencialmente noutro.
                        // Larguras RELATIVAS (RelativeItem), nunca fixas em pontos — ver histórico
                        // desta função para o porquê (larguras fixas já causaram um layout
                        // impossível de resolver, que travava a geração do PDF indefinidamente).
                        col.Item().PaddingTop(8).Row(row =>
                        {
                            void Campo(RowDescriptor r, float peso, string rotulo, string? valor)
                            {
                                r.RelativeItem(peso).Row(rr =>
                                {
                                    rr.AutoItem().Text($"{rotulo}: ").FontSize(9).Bold();
                                    if (string.IsNullOrWhiteSpace(valor))
                                        rr.RelativeItem().Height(14).AlignBottom()
                                            .BorderBottom(0.75f).BorderColor(Colors.Grey.Darken1);
                                    else
                                        rr.RelativeItem().Text(valor).FontSize(9);
                                });
                            }

                            Campo(row, 2.6f, "Escola", escola?.Nome);
                            row.ConstantItem(10);
                            Campo(row, 2.2f, "Agrupamento", escola?.Agrupamento?.Nome);
                            row.ConstantItem(10);
                            Campo(row, 1.3f, "Data", null);
                            row.ConstantItem(10);
                            Campo(row, 2.6f, "Responsável pelo Inventário", responsavel);
                        });
                    });
                });

                // ---- CONTEÚDO: uma secção/tabela por categoria de equipamento ----
                page.Content().PaddingTop(6).Column(mainCol =>
                {
                    void Secao(string titulo, int contagem) =>
                        mainCol.Item().PaddingTop(10).PaddingBottom(3).Text($"{titulo}  ({contagem})")
                            .FontSize(11).Bold().FontColor(corPrimaria);

                    // Tabela de categoria com colunas fixas (Tipo/Marca/Modelo/Nº Série/Nº Inventário)
                    // + uma coluna por característica específica do grupo (dinâmico, conforme o que
                    // estiver configurado em Dados Fixos nesse momento).
                    void TabelaCategoria(List<Equipamento> itens, List<CaracteristicaEquipamento> caracteristicas, int linhasMinimas)
                    {
                        var totalLinhas = Math.Max(itens.Count, linhasMinimas);

                        mainCol.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(1.5f);  // Tipo
                                cols.RelativeColumn(1.2f);  // Marca
                                cols.RelativeColumn(1.3f);  // Modelo
                                cols.RelativeColumn(1.4f);  // Nº de Série
                                cols.RelativeColumn(1.4f);  // Nº de Inventário
                                foreach (var _ in caracteristicas) cols.RelativeColumn(1.3f);
                            });

                            table.Header(h =>
                            {
                                void Cabecalho(string texto) =>
                                    h.Cell().Background(corPrimaria).Padding(5).AlignMiddle()
                                        .Text(texto).FontSize(7.5f).Bold().FontColor(branco);

                                Cabecalho("Tipo");
                                Cabecalho("Marca");
                                Cabecalho("Modelo");
                                Cabecalho("Nº de Série");
                                Cabecalho("Nº de Inventário");
                                foreach (var car in caracteristicas) Cabecalho(car.Nome);
                            });

                            for (var i = 0; i < totalLinhas; i++)
                            {
                                var eq = i < itens.Count ? itens[i] : null;
                                var bg = i % 2 == 0 ? branco : Colors.Grey.Lighten5;
                                IContainer Cel(IContainer c) => c.Background(bg).Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                                    .Padding(4).MinHeight(24).AlignMiddle();

                                table.Cell().Element(Cel).Text(eq?.Tipo ?? "").FontSize(8);
                                table.Cell().Element(Cel).Text(eq?.Marca ?? "").FontSize(8);
                                table.Cell().Element(Cel).Text(eq?.Modelo ?? "").FontSize(8);
                                table.Cell().Element(Cel).Text(eq?.NumeroSerie ?? "").FontSize(8);
                                table.Cell().Element(Cel).Text(eq?.NumeroInventario ?? "").FontSize(8);
                                foreach (var car in caracteristicas)
                                    table.Cell().Element(Cel).Text(eq == null ? "" : ValorCaracteristica(eq, car) ?? "").FontSize(7.5f);
                            }
                        });
                    }

                    Secao("💻 Computadores (Secretária / Portátil / Servidor)", computadores.Count);
                    TabelaCategoria(computadores, caracteristicasComputador, 8);

                    Secao("🖥️ Monitores", monitores.Count);
                    TabelaCategoria(monitores, caracteristicasMonitor, 5);

                    Secao("🌐 Equipamento de Rede (Switch / Router / Access Point)", rede.Count);
                    TabelaCategoria(rede, caracteristicasRede, 5);

                    Secao("🖨️ Impressoras / Multifunções", impressoras.Count);
                    TabelaCategoria(impressoras, caracteristicasImpressora, 5);

                    // "Outros Equipamentos": tipos muito heterogéneos (Câmara CCTV, Projetor, Tablet,
                    // UPS, Telefone IP, etc.) sem um esquema de características comum — por isso
                    // mantém-se aqui o desenho mais genérico, com Localização/Sala, Estado e
                    // Observações, em vez de colunas de características específicas.
                    Secao("📦 Outros Equipamentos (Câmaras, Projetores, Tablets, UPS, etc.)", outros.Count);
                    var totalOutros = Math.Max(outros.Count, 6);
                    mainCol.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1.6f);  // Tipo
                            cols.RelativeColumn(1.2f);  // Marca
                            cols.RelativeColumn(1.3f);  // Modelo
                            cols.RelativeColumn(1.4f);  // Nº de Série
                            cols.RelativeColumn(1.4f);  // Nº de Inventário
                            cols.RelativeColumn(1.2f);  // Localização/Sala
                            cols.RelativeColumn(1.1f);  // Estado
                            cols.RelativeColumn(2.0f);  // Observações
                        });

                        table.Header(h =>
                        {
                            void Cabecalho(string texto) =>
                                h.Cell().Background(corPrimaria).Padding(5).AlignMiddle()
                                    .Text(texto).FontSize(7.5f).Bold().FontColor(branco);

                            Cabecalho("Tipo");
                            Cabecalho("Marca");
                            Cabecalho("Modelo");
                            Cabecalho("Nº de Série");
                            Cabecalho("Nº de Inventário");
                            Cabecalho("Localização / Sala");
                            Cabecalho("Estado");
                            Cabecalho("Observações");
                        });

                        for (var i = 0; i < totalOutros; i++)
                        {
                            var eq = i < outros.Count ? outros[i] : null;
                            var bg = i % 2 == 0 ? branco : Colors.Grey.Lighten5;
                            IContainer Cel(IContainer c) => c.Background(bg).Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                                .Padding(4).MinHeight(24).AlignMiddle();

                            table.Cell().Element(Cel).Text(eq?.Tipo ?? "").FontSize(8);
                            table.Cell().Element(Cel).Text(eq?.Marca ?? "").FontSize(8);
                            table.Cell().Element(Cel).Text(eq?.Modelo ?? "").FontSize(8);
                            table.Cell().Element(Cel).Text(eq?.NumeroSerie ?? "").FontSize(8);
                            table.Cell().Element(Cel).Text(eq?.NumeroInventario ?? "").FontSize(8);
                            table.Cell().Element(Cel).Text("").FontSize(8); // Sala não é um dado gravado — sempre em branco
                            table.Cell().Element(Cel).Text(eq?.Estado ?? "").FontSize(8);
                            table.Cell().Element(Cel).Text(eq?.Observacoes ?? "").FontSize(7.5f);
                        }
                    });
                });

                // ---- RODAPÉ ----
                page.Footer().PaddingTop(4).Column(col =>
                {
                    col.Item().Height(1).Background(corAcento);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("DISIA — Município de Leiria  |  Gerado pelo DISIA Manager")
                            .FontSize(7).FontColor(corTextoSub).Italic();
                        row.ConstantItem(100).AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(x => x.FontSize(7).FontColor(corTextoSub));
                            t.Span("Pág. ");
                            t.CurrentPageNumber();
                            t.Span(" / ");
                            t.TotalPages();
                        });
                    });
                });
            });
        }).GeneratePdf(caminhoDestino);
    }

    /// <summary>Replica fielmente Views/EquipamentoEditWindow.ObterGrupoCaracteristicas: usa o grupo
    /// gravado em Dados Fixos para este Tipo (<paramref name="grupoPorTipo"/>, já carregado uma
    /// única vez pelo chamador) e, só quando não existir (tipos por omissão nunca configurados
    /// explicitamente), recorre às mesmas listas fixas de reserva.</summary>
    private static string ObterGrupoCaracteristicas(string? tipo, Dictionary<string, string?> grupoPorTipo)
    {
        if (string.IsNullOrWhiteSpace(tipo)) return GruposCaracteristicasEquipamento.Generico;

        if (grupoPorTipo.TryGetValue(tipo, out var grupoGravado) && !string.IsNullOrWhiteSpace(grupoGravado))
            return grupoGravado;

        if (TiposComputador.Contains(tipo)) return GruposCaracteristicasEquipamento.Computador;
        if (TiposMonitor.Contains(tipo)) return GruposCaracteristicasEquipamento.Monitor;
        if (TiposImpressora.Contains(tipo)) return GruposCaracteristicasEquipamento.Impressora;
        if (TiposRede.Contains(tipo)) return GruposCaracteristicasEquipamento.Rede;
        if (TiposCamera.Contains(tipo)) return GruposCaracteristicasEquipamento.Camera;
        if (TiposProjetor.Contains(tipo)) return GruposCaracteristicasEquipamento.Projetor;
        return GruposCaracteristicasEquipamento.Generico;
    }

    private static readonly string[] TiposComputador = { "Computador de Secretária", "Portátil", "Servidor" };
    private static readonly string[] TiposMonitor = { "Monitor" };
    private static readonly string[] TiposImpressora = { "Impressora", "Multifunções" };
    private static readonly string[] TiposRede = { "Switch", "Router", "Access Point" };
    private static readonly string[] TiposCamera = { "Câmara CCTV" };
    private static readonly string[] TiposProjetor = { "Projetor", "Quadro Interativo" };

    /// <summary>Nome da característica (exatamente como aparece em Dados Fixos — ver
    /// Data/DbInitializer.MigrarCaracteristicasFixasEmbutidas) → como ler o valor já gravado
    /// diretamente da propriedade correspondente em <see cref="Equipamento"/>, para as
    /// características que têm campo fixo próprio no formulário de Inserir/Editar Equipamento.
    /// Uma característica cujo nome não apareça aqui (ex.: uma criada livremente pelo
    /// administrador) tem o valor lido de <see cref="EquipamentoCaracteristicaValor"/> em vez
    /// disto — ver uso em <see cref="GerarFolhaInventarioPdf"/>.</summary>
    private static readonly Dictionary<string, Func<Equipamento, string?>> ResolvedoresCaracteristicasEmbutidas = new()
    {
        ["Processador"] = e => e.Processador,
        ["Tipo de Memória"] = e => e.TipoMemoria,
        ["Memória (GB)"] = e => e.QuantidadeMemoriaGB?.ToString(),
        ["Tipo de Disco"] = e => e.TipoDisco,
        ["Tamanho do Disco (GB)"] = e => e.TamanhoDiscoGB?.ToString(),
        ["Sistema Operativo"] = e => e.SistemaOperativo,
        ["Tipo de Painel"] = e => e.TipoPainelMonitor,
        ["Polegadas"] = e => e.PolegadasMonitor?.ToString("0.#"),
        ["Resolução"] = e => e.ResolucaoMonitor,
        ["Nº de Portas"] = e => e.NumeroPortas?.ToString(),
        ["Velocidade"] = e => e.VelocidadeRede,
        ["Tipo de Impressora"] = e => e.TipoImpressora,
    };

    private static bool IsJardimInfancia(string? tipo) =>
        tipo != null && tipo.Contains("Jardim", StringComparison.OrdinalIgnoreCase);
}
