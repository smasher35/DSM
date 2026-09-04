using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
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

/// <summary>Gera o Relatório Mensal de Atividades em PDF, com um visual profissional e moderno —
/// capa, sumário/índice, intervenções escolares (com gráficos), gestão da plataforma SIGA,
/// atividades da DISIA e reflexão crítica. Substitui, para quem preferir um PDF em vez de um
/// Word simples, o <see cref="RelatorioService.GerarRelatorioMensal"/> já existente.</summary>
public partial class RelatorioService
{
    private static readonly string[] CoresCapa = { "#0F2A44", "#1F4E79", "#2AB7CA" };

    // 2.3: chaves de secção usadas para o índice mostrar o número de página real de cada bloco,
    // através de QuestPDF Section(...) / BeginPageNumberOfSection(...).
    private const string SecaoSumario = "toc-sumario";
    private const string SecaoIntervencoes = "toc-intervencoes";
    private const string SecaoIntervencoesAgrupamento = "toc-intervencoes-agrupamento";
    private const string SecaoIntervencoesTipo = "toc-intervencoes-tipo";
    private const string SecaoSiga = "toc-siga";
    private const string SecaoSigaAtividades = "toc-siga-atividades";
    private const string SecaoSigaResumo = "toc-siga-resumo";
    private const string SecaoAtividadesDisia = "toc-atividades-disia";
    private const string SecaoReflexao = "toc-reflexao";
    private const string SecaoBalanco = "toc-balanco";
    private const string SecaoDesafios = "toc-desafios";
    private const string SecaoPropostas = "toc-propostas";
    private const string SecaoNotaFinal = "toc-notafinal";

    public void GerarRelatorioMensalPdf(string caminhoDestino, int ano, int mes,
        string autor, string divisao, string telefone, string email)
    {
        var documento = ComposeDocumentoMensal(ano, mes, autor, divisao, telefone, email);
        documento.GeneratePdf(caminhoDestino);
    }

    /// <summary>2.4: gera o relatório mensal em Word (.docx) com EXATAMENTE a mesma estrutura,
    /// dados e aspeto visual do PDF profissional — capa, sumário/índice, gráficos, gestão SIGA,
    /// atividades DISIA e reflexão crítica — em vez de reconstruir tudo de novo com formatação
    /// nativa do Word (que nunca ficava fiel ao PDF). Reaproveita a mesma composição QuestPDF
    /// (<see cref="ComposeDocumentoMensal"/>, os mesmos dados da aplicação) e rasteriza cada página
    /// exatamente como sai no PDF, inserindo depois cada página como uma imagem de página inteira
    /// num documento Word — a abordagem mais simples e mais fiel para "gerar um PDF e converter
    /// para Word" sem depender de nenhuma biblioteca externa de conversão PDF→Word.
    ///
    /// 2.5: SEM UTILIZAÇÃO ATUAL — o botão "Gerar Relatório Mensal (.docx)" (ver
    /// Views/RelatoriosWindow.xaml.cs, GerarMensal_Click) voltou a usar
    /// <see cref="RelatorioService.GerarRelatorioMensal"/> (conteúdo nativo do Word), porque este
    /// método, ao inserir cada página como uma "fotografia" de página inteira, produzia um
    /// documento totalmente impossível de selecionar ou editar (não havia texto real nenhum), e a
    /// combinação de uma imagem a ocupar a página toda com uma quebra de página manual a seguir
    /// estava a originar uma página em branco extra a mais por secção. Mantido apenas para
    /// referência/eventual reutilização futura — não apagar sem ponderar se ainda é preciso.</summary>
    public void GerarRelatorioMensalWord(string caminhoDestino, int ano, int mes,
        string autor, string divisao, string telefone, string email)
    {
        var documento = ComposeDocumentoMensal(ano, mes, autor, divisao, telefone, email);

        var pastaTemp = Path.Combine(Path.GetTempPath(), "DISIA_RelatorioMensal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pastaTemp);
        try
        {
            // Rasteriza cada página do PDF em alta resolução (300 DPI), preservando integralmente
            // o layout, cores, gráficos e capa do relatório profissional.
            var configuracaoImagem = new QuestPDF.Infrastructure.ImageGenerationSettings
            {
                ImageFormat = QuestPDF.Infrastructure.ImageFormat.Png,
                RasterDpi = 300
            };
            documento.GenerateImages(indice => Path.Combine(pastaTemp, $"pagina_{indice:0000}.png"), configuracaoImagem);

            var ficheirosImagens = Directory.GetFiles(pastaTemp, "pagina_*.png").OrderBy(f => f).ToList();

            using var doc = WordprocessingDocument.Create(caminhoDestino, WordprocessingDocumentType.Document);
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new OxmlDocument();
            var body = mainPart.Document.AppendChild(new Body());

            for (var i = 0; i < ficheirosImagens.Count; i++)
            {
                var bytes = File.ReadAllBytes(ficheirosImagens[i]);
                AdicionarImagemPaginaCompleta(mainPart, body, bytes);
                if (i < ficheirosImagens.Count - 1)
                    body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            }

            // Página A4 (retrato), sem margens — cada imagem já inclui as suas próprias margens e
            // faixas decorativas tal como saem no PDF, por isso a página do Word não deve
            // acrescentar margens próprias (senão a imagem ficava "encolhida" dentro de margens
            // duplicadas).
            body.AppendChild(new SectionProperties(
                new DocumentFormat.OpenXml.Wordprocessing.PageSize { Width = 11906, Height = 16838, Orient = PageOrientationValues.Portrait },
                new PageMargin { Top = 0, Right = 0, Bottom = 0, Left = 0, Header = 0, Footer = 0, Gutter = 0 }));

            mainPart.Document.Save();
        }
        finally
        {
            try { Directory.Delete(pastaTemp, recursive: true); } catch { /* pasta temporária — falha a apagar não é crítica */ }
        }
    }

    /// <summary>Insere uma imagem (uma página rasterizada do PDF) a ocupar uma página A4 inteira
    /// (21 × 29,7 cm), centrada, sem legenda — usado exclusivamente por
    /// <see cref="GerarRelatorioMensalWord"/> para reproduzir cada página do PDF profissional.</summary>
    private static void AdicionarImagemPaginaCompleta(MainDocumentPart mainPart, Body body, byte[] bytes)
    {
        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(bytes))
            imagePart.FeedData(stream);
        var relationshipId = mainPart.GetIdOfPart(imagePart);

        const long larguraEmu = 7560000L;  // 21 cm (largura A4)
        const long alturaEmu = 10692000L;  // 29,7 cm (altura A4)

        var elemento = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = larguraEmu, Cy = alturaEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = (UInt32Value)1U, Name = "Página" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "Página" },
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
            new ParagraphProperties(new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { Before = "0", After = "0" }),
            new Run(elemento)));
    }

    /// <summary>Constrói o documento QuestPDF do Relatório Mensal de Atividades (capa, sumário,
    /// intervenções escolares, gestão SIGA, atividades DISIA, reflexão crítica) sem ainda o gerar
    /// em nenhum formato de saída. Usado por <see cref="GerarRelatorioMensalPdf"/> (saída .pdf) e
    /// por <see cref="GerarRelatorioMensalWord"/> (saída .docx, com cada página rasterizada a
    /// partir deste mesmo documento) — 2.4: garante que o Word e o PDF ficam sempre exatamente
    /// iguais, porque são gerados a partir da mesma composição e dos mesmos dados.</summary>
    private QuestPDF.Fluent.Document ComposeDocumentoMensal(int ano, int mes,
        string autor, string divisao, string telefone, string email)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // ---------------------------------------------------------------
        // Recolha de dados
        // ---------------------------------------------------------------
        var totalEdificiosAtivos = _db.Escolas.Count(e => e.Estado != EstadosEscola.Desativada);
        var todosAgrupamentos = _db.Agrupamentos.OrderBy(a => a.Nome).ToList();

        var intervencoes = _db.Intervencoes
            .Include(i => i.Agrupamento)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .Where(i => i.Ano == ano && i.Mes == mes && i.Estado != EstadoIntervencao.Cancelada)
            .ToList();

        // Todos os agrupamentos aparecem na tabela, mesmo com 0 intervenções — a distribuição por
        // estado (Fechadas/Pendentes/Em Progresso/Em Espera) usa a mesma regra do "Resumo de
        // Intervenções por Agrupamento" (ver GerarResumoIntervencoesPorAgrupamento), para a secção
        // "Intervenções Escolares" poder mostrar exatamente o mesmo gráfico + tabela desse resumo.
        var porAgrupamento = todosAgrupamentos
            .Select(a => new
            {
                Agrupamento = a.Nome,
                Total = intervencoes.Count(i => i.AgrupamentoId == a.Id),
                Fechadas = intervencoes.Count(i => i.AgrupamentoId == a.Id && i.Estado == EstadoIntervencao.Fechada),
                Pendentes = intervencoes.Count(i => i.AgrupamentoId == a.Id && i.Estado == EstadoIntervencao.Pendente),
                EmProgresso = intervencoes.Count(i => i.AgrupamentoId == a.Id && i.Estado == EstadoIntervencao.EmProgresso),
                EmEspera = intervencoes.Count(i => i.AgrupamentoId == a.Id && i.Estado == EstadoIntervencao.EmEspera)
            })
            .ToList();
        var semAgrupamento = intervencoes.Count(i => i.AgrupamentoId == null);
        if (semAgrupamento > 0)
            porAgrupamento = porAgrupamento.Append(new
            {
                Agrupamento = "(Sem Agrupamento)",
                Total = semAgrupamento,
                Fechadas = intervencoes.Count(i => i.AgrupamentoId == null && i.Estado == EstadoIntervencao.Fechada),
                Pendentes = intervencoes.Count(i => i.AgrupamentoId == null && i.Estado == EstadoIntervencao.Pendente),
                EmProgresso = intervencoes.Count(i => i.AgrupamentoId == null && i.Estado == EstadoIntervencao.EmProgresso),
                EmEspera = intervencoes.Count(i => i.AgrupamentoId == null && i.Estado == EstadoIntervencao.EmEspera)
            }).ToList();

        var porCategoria = intervencoes
            .SelectMany(i => i.Categorias)
            .Where(c => c.Categoria != null)
            .GroupBy(c => c.Categoria!.Nome)
            .Select(g => new { Categoria = g.Key, Total = g.Sum(x => x.Quantidade) })
            .OrderByDescending(g => g.Total)
            .ToList();

        // Cruzamento Tipo × Agrupamento, só para agrupamentos com pelo menos uma intervenção no mês
        // (para não sobrecarregar visualmente o gráfico com barras a 0).
        var agrupamentosComIntervencoes = intervencoes
            .Where(i => i.Agrupamento != null)
            .Select(i => i.Agrupamento!.Nome)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
        var categoriasDoMes = porCategoria.Select(c => c.Categoria).ToList();
        // Cor própria por categoria (a mesma usada em toda a aplicação — ver
        // CategoriaIntervencao.CorHex e Views/CategoriasIntervencaoWindow.xaml): no gráfico "Tipos
        // de Intervenção por Agrupamento", o eixo X agrupa por AGRUPAMENTO e cada tipo mantém
        // sempre a mesma cor dentro de cada grupo — ver GraficoBarrasAgrupadas.
        var coresPorCategoria = _db.CategoriasIntervencao
            .ToDictionary(c => c.Nome, c => c.CorHex, StringComparer.OrdinalIgnoreCase);
        var cruzamentoTipoAgrupamento = categoriasDoMes.Select(cat => (
            Categoria: cat,
            Valores: (IReadOnlyList<int>)agrupamentosComIntervencoes.Select(agr =>
                intervencoes.Count(i => i.Agrupamento?.Nome == agr && i.Categorias.Any(c => c.Categoria?.Nome == cat))
            ).ToList()
        )).ToList();

        var atividadesDisia = _db.AtividadesDisia
            .Include(a => a.Categoria)
            .Where(a => a.Ano == ano && a.Mes == mes)
            .OrderBy(a => a.Categoria!.Nome).ThenBy(a => a.Data)
            .ToList();

        var dadosSiga = _db.RelatoriosMensaisDados.FirstOrDefault(r => r.Ano == ano && r.Mes == mes)
            ?? new RelatorioMensalDados { Ano = ano, Mes = mes };

        // Resumo automático "estilo dashboard" (substitui a antiga captura de ecrã do Excel),
        // usando os dados reais da aplicação em vez de uma imagem estática.
        var anoCorrente = ano;
        var totalIntervencoesAno = _db.Intervencoes.Count(i => i.Ano == anoCorrente && i.Estado != EstadoIntervencao.Cancelada);
        var totalIntervencoesHistorico = _db.Intervencoes.Count(i => i.Estado != EstadoIntervencao.Cancelada);
        var pendentesDisia = _db.EquipamentosRecolhidos.Count(r => r.DataEntrega == null);
        var porMesAno = Enumerable.Range(1, 12).Select(m => new
        {
            Mes = NomesMeses[m].Length >= 3 ? NomesMeses[m][..3] : NomesMeses[m],
            Total = _db.Intervencoes.Count(i => i.Ano == anoCorrente && i.Mes == m && i.Estado != EstadoIntervencao.Cancelada)
        }).ToList();

        var mesFormatado = $"{NomesMeses[mes]} de {ano}";

        // Vista Geral do Dashboard, capturada secção a secção (gauges, gráficos de barras, pizza e
        // agrupamento) em vez de uma única imagem gigante — ver DashboardSnapshotService.CapturarSeccoes
        // — para o relatório poder organizar cada par de gráficos numa linha compacta e forçar a
        // secção "Intervenções por Agrupamento" para uma página própria (ver ComposeGestaoSiga).
        var dashboardSeccoes = DashboardSnapshotService.CapturarSeccoes();

        // ---------------------------------------------------------------
        // Composição do documento
        // ---------------------------------------------------------------
        return PdfDocument.Create(container =>
        {
            // ---- Página 1: Capa ----
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.Content().Element(c => ComposeCapaMensal(c, autor, divisao, telefone, email, mesFormatado));
            });

            // ---- Páginas seguintes: conteúdo corrido, sempre em A4 retrato, com cabeçalho/rodapé
            // e numeração. ----
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.6f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(9).FontColor(Colors.Grey.Darken3));

                page.Header().Element(h => ComposeCabecalhoMensal(h, mesFormatado));
                page.Footer().Element(ComposeRodapeMensal);

                page.Content().Element(c => c.Column(col =>
                {
                    ComposeSumarioIndice(col, autor, divisao, mesFormatado);
                    col.Item().PageBreak();

                    ComposeIntervencoesEscolares(col, totalEdificiosAtivos, todosAgrupamentos.Count,
                        porAgrupamento.Select(a => (a.Agrupamento, a.Total, a.Fechadas, a.Pendentes, a.EmProgresso, a.EmEspera)).ToList(),
                        porCategoria.Select(c => (c.Categoria, c.Total)).ToList(),
                        agrupamentosComIntervencoes, cruzamentoTipoAgrupamento, coresPorCategoria, mesFormatado);
                    col.Item().PageBreak();

                    ComposeGestaoSiga(col, dadosSiga, totalIntervencoesAno, totalIntervencoesHistorico, pendentesDisia,
                        porMesAno.Select(m => (m.Mes, m.Total)).ToList(), dashboardSeccoes);
                    col.Item().PageBreak();

                    ComposeAtividadesDisia(col, atividadesDisia, mesFormatado);
                    col.Item().PageBreak();

                    ComposeReflexaoCritica(col, dadosSiga, mesFormatado);
                }));
            });
        });
    }
}
