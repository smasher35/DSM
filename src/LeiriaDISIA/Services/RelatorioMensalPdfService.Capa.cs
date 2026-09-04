using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LeiriaDISIA.Services;

public partial class RelatorioService
{
    /// <summary>Capa moderna e tecnológica: fundo com duas faixas diagonais (o essencial da
    /// composição é feito em SVG, para não depender de nenhuma imagem externa), com o logótipo do
    /// Município, os dados de contacto do autor e o mês/ano do relatório. Usa três camadas
    /// alinhadas ao topo/meio/fundo (em vez de espaçamentos manuais) para se manter robusta seja
    /// qual for o comprimento exato de cada linha de texto.</summary>
    private static void ComposeCapaMensal(IContainer container, string autor, string divisao,
        string telefone, string email, string mesFormatado)
    {
        var a4 = PageSizes.A4;
        const string corNavy = "#0F2A44";
        const string corTeal = "#2AB7CA";
        // Azul claro e bem saturado para o título principal — mais "presente"/com mais carácter do
        // que branco liso, mantendo a coerência com o azul usado no resto do relatório, mas ainda
        // com bom contraste sobre a faixa navy de fundo.
        const string corTituloCapa = "#5DADE2";

        container.Width(a4.Width).Height(a4.Height).Layers(layers =>
        {
            // Fundo: faixa diagonal escura no topo, branco no meio, faixa diagonal clara no fundo.
            layers.PrimaryLayer().Svg(size =>
            {
                // Formata todas as coordenadas com CultureInfo.InvariantCulture: a interpolação de
                // string usa por omissão a cultura atual do sistema, e num Windows configurado em
                // português (separador decimal ",") um valor como 353.59 seria escrito "353,59" —
                // o que parte o atributo "points" do SVG (que espera "," a separar X de Y dentro de
                // cada ponto) e faz as faixas diagonais deixarem de aparecer corretamente, ficando
                // a capa praticamente em branco por trás do título.
                string F(double v) => v.ToString(CultureInfo.InvariantCulture);

                return $"""
                    <svg width="{F(size.Width)}" height="{F(size.Height)}" xmlns="http://www.w3.org/2000/svg">
                        <rect x="0" y="0" width="{F(size.Width)}" height="{F(size.Height)}" fill="#FFFFFF" />
                        <polygon points="0,0 {F(size.Width)},0 {F(size.Width)},{F(size.Height * 0.30)} 0,{F(size.Height * 0.42)}" fill="{corNavy}" />
                        <polygon points="0,{F(size.Height * 0.42)} {F(size.Width)},{F(size.Height * 0.30)} {F(size.Width)},{F(size.Height * 0.335)} 0,{F(size.Height * 0.455)}" fill="{corTeal}" />
                        <polygon points="0,{F(size.Height * 0.90)} {F(size.Width)},{F(size.Height * 0.82)} {F(size.Width)},{F(size.Height)} 0,{F(size.Height)}" fill="{corNavy}" />
                        <polygon points="0,{F(size.Height * 0.885)} {F(size.Width)},{F(size.Height * 0.805)} {F(size.Width)},{F(size.Height * 0.82)} 0,{F(size.Height * 0.90)}" fill="{corTeal}" />
                        <!-- Painel suave por trás do conteúdo: assegura contraste com texto escuro
                             e destaca o logótipo de fundo branco. -->
                        <rect x="34" y="{F(size.Height * 0.48)}" width="{F(size.Width - 68)}" height="{F(size.Height * 0.27)}" rx="14" fill="#EAF3F8" stroke="#B9D7E5" stroke-width="1" />
                    </svg>
                    """;
            });

            // Título — alinhado ao topo, dentro da faixa escura.
            layers.Layer().AlignTop().Padding(50).Column(col =>
            {
                col.Item().Text("RELATÓRIO DE ATIVIDADES").FontSize(30).Bold().FontColor(corTituloCapa);
                col.Item().PaddingTop(6).Text("DISIA — Divisão de Sistemas de Informação e Aplicações")
                    .FontSize(13).FontColor(Colors.White).Italic();
            });

            // Logótipo + dados de contacto — centrados na zona branca REAL da capa (entre as duas
            // faixas diagonais definidas acima), em vez de no meio de toda a página: antes disso,
            // como o bloco tem menos altura do que a zona branca e ficava centrado em relação à
            // página inteira, acabava a aparecer demasiado alto, quase a tocar a faixa superior.
            var zonaBrancaTopo = a4.Height * 0.455f;
            var zonaBrancaFundo = a4.Height * 0.805f;
            const float alturaEstimadaConteudo = 145f; // logótipo + 4 linhas de texto + espaçamentos
            var paddingTopoConteudo = zonaBrancaTopo + (zonaBrancaFundo - zonaBrancaTopo - alturaEstimadaConteudo) / 2f;

            layers.Layer().PaddingTop(paddingTopoConteudo).PaddingHorizontal(50).Column(col =>
            {
                col.Item().Height(50).AlignLeft().Width(220).Image(AppAssets.LogoMunicipio).FitArea();
                col.Item().PaddingTop(26).Text(autor).FontSize(14).Bold().FontColor(Colors.Grey.Darken4);
                col.Item().PaddingTop(2).Text(divisao).FontSize(10).FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(6).Text($"Telefone: {telefone}").FontSize(10).FontColor(Colors.Grey.Darken2);
                col.Item().Text($"Email: {email}").FontSize(10).FontColor(Colors.Grey.Darken2);
            });

            // Mês/Ano do relatório — alinhado ao fundo, dentro da faixa escura inferior.
            layers.Layer().AlignBottom().Padding(50).Text(mesFormatado)
                .FontSize(18).Bold().FontColor(Colors.White);
        });
    }

    private static void ComposeCabecalhoMensal(IContainer container, string mesFormatado)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("MUNICÍPIO DE LEIRIA — DISIA").FontSize(8).FontColor(Colors.Grey.Darken1).LetterSpacing(0.06f);
                    c.Item().Text("Relatório de Atividades").FontSize(15).Bold().FontColor(Colors.Blue.Darken2);
                });
                row.ConstantItem(150).AlignRight().AlignMiddle().Text(mesFormatado)
                    .FontSize(10).FontColor(Colors.Grey.Darken1).Italic();
                row.ConstantItem(34).PaddingLeft(10).Height(34).Image(AppAssets.LogoDisia).FitArea();
            });
            col.Item().PaddingTop(6).Height(2.5f).Background(Colors.Blue.Darken2);
            col.Item().Height(1).Background(Colors.Blue.Lighten2);
            col.Item().PaddingBottom(6);
        });
    }

    private static void ComposeRodapeMensal(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Height(1).Background(Colors.Blue.Lighten2);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text("Câmara Municipal de Leiria — DISIA").FontSize(7).FontColor(Colors.Grey.Darken1).Italic();
                row.ConstantItem(100).AlignRight().Text(t =>
                {
                    // 2.3: DefaultTextStyle garante que "Página", o nº atual e o total usam sempre
                    // exatamente o mesmo tamanho de letra, independentemente do nº de dígitos —
                    // antes, alguns números de página apareciam maiores/menores que os outros.
                    t.DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Darken1));
                    t.Span("Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        });
    }

    private static void ComposeSumarioIndice(ColumnDescriptor col, string autor, string divisao, string mesFormatado)
    {
        col.Item().Section(SecaoSumario).Text("Sumário").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
        col.Item().PaddingTop(6).PaddingBottom(18);
        foreach (var paragrafo in DividirEmParagrafos(
            $"Relatório de atividades desempenhadas no mês de {mesFormatado}, por {autor}, ao serviço da {divisao}."))
        {
            col.Item().PaddingBottom(4).Text(paragrafo).FontSize(10.5f).FontColor(Colors.Grey.Darken3).Justify();
        }

        col.Item().Text("Índice").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
        col.Item().PaddingTop(10);

        // 2.3: cada item do índice mostra o número de página real da respetiva secção, através das
        // marcações Section(...) colocadas no início de cada bloco de conteúdo (ver constantes
        // Secao* e ComposePaginaNumeroIndice), em vez de uma lista estática sem referência de página.
        // O texto de cada item (título/subitem) mantém tamanhos diferentes por nível, mas o número
        // de página em si usa sempre o mesmo tamanho — antes seguia o mesmo "tamanho" do texto, o
        // que fazia os números de página aparecerem maiores nos títulos e menores nos subitens.
        const float tamanhoPaginaIndice = 9.5f;

        void ItemIndice(string texto, string chaveSeccao, bool subitem = false)
        {
            var tamanho = subitem ? 9.5f : 11f;
            col.Item().PaddingLeft(subitem ? 16 : 0).PaddingBottom(6).SectionLink(chaveSeccao).Row(row =>
            {
                if (subitem)
                    row.RelativeItem().Text(texto).FontSize(tamanho).FontColor(Colors.Grey.Darken2);
                else
                    row.RelativeItem().Text(texto).FontSize(tamanho).Bold().FontColor(Colors.Grey.Darken4);

                row.AutoItem().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(tamanhoPaginaIndice).FontColor(Colors.Grey.Darken1));
                    text.Span("pág. ");
                    text.BeginPageNumberOfSection(chaveSeccao);
                });
            });
        }

        ItemIndice("Sumário", SecaoSumario);
        ItemIndice("Intervenções Escolares", SecaoIntervencoes);
        ItemIndice("Total de Intervenções por Agrupamento", SecaoIntervencoesAgrupamento, true);
        ItemIndice("Totais por Tipo de Intervenção", SecaoIntervencoesTipo, true);
        ItemIndice("Gestão da Plataforma SIGA", SecaoSiga);
        ItemIndice("Atividades na Plataforma SIGA", SecaoSigaAtividades, true);
        ItemIndice("Resumo das Intervenções nas Escolas", SecaoSigaResumo, true);
        ItemIndice("Atividades na DISIA", SecaoAtividadesDisia);
        ItemIndice("Reflexão Crítica", SecaoReflexao);
        ItemIndice("Balanço Geral do Mês", SecaoBalanco, true);
        ItemIndice("Principais Desafios e Constrangimentos", SecaoDesafios, true);
        ItemIndice("Propostas de Melhoria para os Próximos Meses", SecaoPropostas, true);
        ItemIndice("Nota Final", SecaoNotaFinal, true);
    }
}
