using LeiriaDISIA.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LeiriaDISIA.Services;

public partial class RelatorioService
{
    private static void ComposeGestaoSiga(ColumnDescriptor col, RelatorioMensalDados dados,
        int totalIntervencoesAno, int totalIntervencoesHistorico, int pendentesDisia,
        IReadOnlyList<(string Mes, int Total)> porMesAno,
        LeiriaDISIA.Views.DashboardView.DashboardSeccoesSnapshot? dashboardSeccoes = null)
    {
        var branco = Colors.White;

        col.Item().Section(SecaoSiga).Text("Gestão Plataforma SIGA").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
        col.Item().PaddingTop(8);
        foreach (var paragrafo in DividirEmParagrafos(
            "No âmbito das minhas funções, tenho a responsabilidade pela gestão da plataforma SIGA (Sistema " +
            "Integrado de Gestão e Aprendizagem), utilizada pelas escolas do 1.º Ciclo e Jardins de Infância do " +
            "concelho de Leiria. " +
            "Esta plataforma constitui uma ferramenta fundamental para a gestão e articulação de processos " +
            "educativos, assegurando a comunicação e a interação entre os diversos intervenientes da comunidade " +
            "educativa, nomeadamente o Município de Leiria, as Juntas de Freguesia, fornecedores, parceiros, " +
            "IPSS, encarregados de educação, agrupamentos de escolas, escolas e jardins de infância, de acordo " +
            "com as respetivas áreas de atuação e competências. " +
            "No exercício desta responsabilidade, asseguro o suporte técnico e funcional da plataforma, " +
            "contribuindo para o correto funcionamento dos serviços e para a eficiência dos processos de " +
            "comunicação e gestão entre todas as entidades envolvidas."))
        {
            col.Item().PaddingBottom(6).Text(paragrafo).FontSize(10).FontColor(Colors.Grey.Darken3).LineHeight(1.3f).Justify();
        }
        col.Item().PaddingBottom(8);

        col.Item().Section(SecaoSigaAtividades).PaddingBottom(6)
            .Text("Atividades na Plataforma SIGA EDUBOX").FontSize(13).Bold().FontColor(Colors.Blue.Darken2);

        void Bullet(string texto) => col.Item().PaddingBottom(5).Row(row =>
        {
            row.ConstantItem(12).Text("•").FontSize(10).Bold().FontColor(Colors.Blue.Darken2);
            row.RelativeItem().Text(texto).FontSize(9.5f).FontColor(Colors.Grey.Darken3).LineHeight(1.25f).Justify();
        });

        Bullet($"Gestão dos tickets do processo educativo, corrigindo os tickets que se encontram no workflow " +
               $"errado, definindo os workflows e corrigindo as tipificações dos pedidos " +
               $"({dados.TotalAlteracaoTipificacao} tickets), com envio para as respetivas Juntas de Freguesia.");
        Bullet($"Correção dos estados dos tickets ({dados.TotalEstadoTickets} tickets) — por exemplo, alterar de " +
               $"estado \"Fechado\" para \"Em Progresso\" — geralmente em pedidos realizados pelos serviços de " +
               $"educação.");
        Bullet($"Alteração de palavras-passe ({dados.TotalAlteracaoPasswords}).");

        col.Item().PaddingTop(6);

        void ImagemOuPlaceholder(byte[]? imagem, string legenda)
        {
            if (imagem is { Length: > 0 })
            {
                col.Item().PaddingBottom(2).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                    .AlignCenter().MaxHeight(260).Image(imagem).FitArea();
            }
            else
            {
                col.Item().PaddingBottom(2).Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten4)
                    .Padding(24).AlignCenter()
                    .Text("(Imagem não anexada para este mês — pode ser adicionada no formulário do relatório.)")
                    .FontSize(8.5f).Italic().FontColor(Colors.Grey.Darken1);
            }

            col.Item().PaddingTop(2).PaddingBottom(12).AlignCenter().Text(legenda).FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);
        }

        ImagemOuPlaceholder(dados.ImagemPedidosSiga, "Figura 4 — Pedidos existentes na Plataforma SIGA.");
        ImagemOuPlaceholder(dados.ImagemWorkflowSiga, "Figura 5 — Workflows da Plataforma SIGA.");

        col.Item().PageBreak();

        // ---- Resumo visual (substitui a antiga captura de ecrã do dashboard em Excel, agora
        // gerado automaticamente a partir dos dados reais da aplicação). ----
        col.Item().Section(SecaoSigaResumo).PaddingBottom(6)
            .Text("Resumo das Intervenções nas Escolas").FontSize(13).Bold().FontColor(Colors.Blue.Darken2);

        col.Item().PaddingBottom(10).Row(row =>
        {
            void Card(RowDescriptor r, string valor, string label, string cor)
            {
                r.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Background(branco).Padding(8).Column(c =>
                {
                    c.Item().AlignCenter().Text(valor).FontSize(20).Bold().FontColor(cor);
                    c.Item().AlignCenter().Text(label).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });
            }

            row.ConstantItem(6);
            Card(row, totalIntervencoesAno.ToString(), "Intervenções no Ano Corrente", Colors.Blue.Darken2);
            row.ConstantItem(6);
            Card(row, totalIntervencoesHistorico.ToString(), "Intervenções (Histórico Total)", Colors.Teal.Darken2);
            row.ConstantItem(6);
            Card(row, pendentesDisia.ToString(), "Equipamento Pendente (Reparação/Entrega)", Colors.Orange.Darken2);
            row.ConstantItem(6);
        });

        GraficoBarras(col, "Intervenções por Mês (Ano Corrente)", porMesAno);
        col.Item().PaddingTop(2).AlignCenter()
            .Text("Figura 6 — Resumo automático das intervenções nas escolas ao longo do ano corrente.")
            .FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);

        // Vista geral do Dashboard da aplicação (KPIs, gauges e gráficos), capturada em tempo real
        // — ver DashboardSnapshotService.CapturarSeccoes. Fica sempre em A4 retrato, como todo o
        // resto do relatório (nunca muda o tamanho/orientação da página): em vez de uma única
        // imagem gigante dividida às cegas em faixas verticais, cada secção do Dashboard é
        // capturada em separado e organizada aqui — os KPIs primeiro, tal como aparecem no ecrã,
        // depois aos pares, lado a lado: "Distribuição do Parque de Equipamento" (gauges) +
        // "Intervenções por Mês" numa linha, "Intervenções por Categoria" (ano + mês corrente)
        // noutra. A secção "Intervenções por Agrupamento" flui naturalmente a seguir (só passa
        // para a página seguinte se realmente não couber na atual — não força uma quebra de
        // página só por si, para não deixar espaço em branco desnecessário na página anterior). Se
        // alguma captura tiver falhado, essa secção (ou toda a "Vista Geral") é omitida em
        // silêncio (o resto do relatório continua a ser gerado normalmente).
        if (dashboardSeccoes is { } seccoes && (seccoes.Kpis is { Length: > 0 } || seccoes.Gauges is { Length: > 0 } ||
            seccoes.ChartPorMes is { Length: > 0 } || seccoes.ChartPorCategoriaAno is { Length: > 0 } ||
            seccoes.ChartPorCategoriaMes is { Length: > 0 } ||
            seccoes.ChartAgrupamentoAno is { Length: > 0 } || seccoes.ChartAgrupamentoMes is { Length: > 0 }))
        {
            col.Item().PageBreak();
            col.Item().Text("Vista Geral do Dashboard").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
            col.Item().PaddingTop(4).PaddingBottom(10)
                .Text("Panorâmica geral do ecossistema — equipamento e intervenções — tal como configurada na aplicação.")
                .FontSize(9).FontColor(Colors.Grey.Darken2);

            if (seccoes.Kpis is { Length: > 0 })
            {
                col.Item().PaddingBottom(10).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                    .Image(seccoes.Kpis).FitWidth();
            }

            // Coloca até duas imagens lado a lado, numa única linha compacta; se só uma das duas
            // existir, ocupa a linha inteira sozinha. Nunca deixa uma secção cortada a meio pela
            // margem da página — cada linha é um único item do relatório.
            void LinhaComDuasImagens(byte[]? esquerda, byte[]? direita)
            {
                if (esquerda is not { Length: > 0 } && direita is not { Length: > 0 }) return;

                col.Item().PaddingBottom(8).Row(row =>
                {
                    if (esquerda is { Length: > 0 })
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Image(esquerda).FitWidth();
                    if (esquerda is { Length: > 0 } && direita is { Length: > 0 })
                        row.ConstantItem(10);
                    if (direita is { Length: > 0 })
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Image(direita).FitWidth();
                });
            }

            // Secções 1 + 2: Distribuição do Parque de Equipamento (gauges) e Intervenções por Mês,
            // lado a lado, de forma compacta.
            LinhaComDuasImagens(seccoes.Gauges, seccoes.ChartPorMes);

            // Secções 3 + 4: Intervenções por Categoria — total anual e mês corrente, lado a lado.
            LinhaComDuasImagens(seccoes.ChartPorCategoriaAno, seccoes.ChartPorCategoriaMes);

            if (seccoes.Gauges is { Length: > 0 } || seccoes.ChartPorMes is { Length: > 0 } ||
                seccoes.ChartPorCategoriaAno is { Length: > 0 } || seccoes.ChartPorCategoriaMes is { Length: > 0 })
            {
                col.Item().PaddingTop(2).AlignCenter()
                    .Text("Figura 7 — Vista geral do Dashboard: equipamento e intervenções por categoria.")
                    .FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);
            }

            // Secção 5: Intervenções por Agrupamento — sem quebra de página forçada; continua na
            // mesma página se ainda houver espaço, e só passa para a seguinte se for preciso.
            if (seccoes.ChartAgrupamentoAno is { Length: > 0 } || seccoes.ChartAgrupamentoMes is { Length: > 0 })
            {
                col.Item().PaddingTop(10).Text("Intervenções por Agrupamento").FontSize(13).Bold().FontColor(Colors.Blue.Darken2);
                col.Item().PaddingTop(4).PaddingBottom(10)
                    .Text("Distribuição das intervenções por agrupamento de escolas, no ano corrente e no mês corrente.")
                    .FontSize(9).FontColor(Colors.Grey.Darken2);

                LinhaComDuasImagens(seccoes.ChartAgrupamentoAno, seccoes.ChartAgrupamentoMes);

                // Legenda (abreviatura = nome completo do agrupamento) escrita como texto normal do
                // PDF — este gráfico, ao contrário dos restantes desta secção, já não é uma captura
                // de ecrã do Dashboard (ver DashboardView.CapturarSeccoes), pelo que a legenda deixa
                // de vir "embutida" na imagem e passa a ser escrita aqui ao lado.
                if (!string.IsNullOrWhiteSpace(seccoes.LegendaChartAgrupamentoAno) || !string.IsNullOrWhiteSpace(seccoes.LegendaChartAgrupamentoMes))
                {
                    col.Item().PaddingTop(2).Row(row =>
                    {
                        row.RelativeItem().Text(seccoes.LegendaChartAgrupamentoAno ?? "")
                            .FontSize(7).Italic().FontColor(Colors.Grey.Darken1);
                        row.ConstantItem(10);
                        row.RelativeItem().Text(seccoes.LegendaChartAgrupamentoMes ?? "")
                            .FontSize(7).Italic().FontColor(Colors.Grey.Darken1);
                    });
                }

                col.Item().PaddingTop(2).AlignCenter()
                    .Text("Figura 8 — Intervenções por Agrupamento (ano corrente e mês corrente).")
                    .FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);
            }
        }
    }
}
